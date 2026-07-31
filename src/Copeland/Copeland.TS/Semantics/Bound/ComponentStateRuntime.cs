namespace Copeland.TS.Semantics.Bound;

/// <summary>
/// One renderer-neutral attachment and its opaque adapter payload after a
/// component presentation has been evaluated.  This is deliberately the same
/// HostAttachmentMir used for initial delivery; state changes do not invent a
/// second renderer update channel.
/// </summary>
public sealed record ComponentPresentationAttachment(HostAttachmentMir Attachment, object? Payload);

public sealed record ComponentPresentationSnapshot(IReadOnlyList<ComponentPresentationAttachment> Attachments)
{
    public static ComponentPresentationSnapshot Empty { get; } = new([]);
}

public sealed record ComponentStateRuntimeDiagnostic(
    string Id,
    string ComponentInstanceId,
    string StateIdentity,
    string? EventName,
    string? AttachmentId,
    string Message);

public sealed record ComponentStateDispatchResult<TState>(
    TState State,
    IReadOnlyList<ComponentStateRuntimeDiagnostic> Diagnostics,
    bool Applied);

/// <summary>
/// Compiler-owned boundaries at which a transition consequence may start.
/// These names intentionally describe Copeland state and attachment facts,
/// not a renderer's private lifecycle.
/// </summary>
public enum ComponentCompletionPhase
{
    StateCommitted,
    PresentationCommitted,
    AttachmentsSettled,
}

public sealed record ComponentEffectDescriptor(
    string Identity,
    ComponentCompletionPhase Phase,
    int AuthoredOrder,
    string? CompletionEventName = null);

/// <summary>
/// The result of reducing one event. Effects are transition consequences, not
/// callbacks retained by a renderer or dependency-tracked subscriptions.
/// </summary>
public sealed record ComponentTransition<TState>(
    TState NextState,
    IReadOnlyList<ComponentEffectRequest<TState>> Effects)
{
    public static ComponentTransition<TState> StateOnly(TState nextState)
        => new(nextState, []);
}

public sealed record ComponentEffectContext<TState>(
    string ComponentInstanceId,
    string StateIdentity,
    string TriggeringEvent,
    string TransitionIdentity,
    ComponentEffectDescriptor Effect,
    TState CommittedState,
    CancellationToken LifetimeCancellation);

/// <summary>
/// A completion is deliberately an event bridge action. It cannot mutate a
/// frame's state directly or obtain an adapter root.
/// </summary>
public sealed class ComponentEffectCompletion<TState>(
    string eventName,
    Action deliver)
{
    public string EventName { get; } = eventName;
    internal void Deliver() => deliver();

    public static ComponentEffectCompletion<TState> ToEvent<TEvent>(
        TEvent @event,
        ComponentEventBridge<TState, TEvent> bridge)
        => new(bridge.EventName, () => bridge.Deliver(@event));
}

public sealed class ComponentEffectRequest<TState>(
    ComponentEffectDescriptor descriptor,
    Func<ComponentEffectContext<TState>, ValueTask<ComponentEffectCompletion<TState>?>> execute)
{
    public ComponentEffectDescriptor Descriptor { get; } = descriptor;
    internal ValueTask<ComponentEffectCompletion<TState>?> Execute(ComponentEffectContext<TState> context) => execute(context);
}

public sealed record ComponentRuntimeTrace(
    string Kind,
    string ComponentInstanceId,
    string? EventName,
    string? TransitionIdentity,
    string? EffectIdentity,
    ComponentCompletionPhase? Phase,
    string? Detail = null);

/// <summary>
/// The runtime frame for one canonical component instance.  The frame owns
/// application state and evaluates presentation locally; renderer adapters
/// receive only the resulting attachment-plan delta.
/// </summary>
public sealed class ComponentStateFrame<TState>
{
    private readonly RendererAttachmentRegistry _attachments;
    private readonly Func<TState, ComponentPresentationSnapshot> _present;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<ComponentRuntimeTrace> _trace = [];
    private readonly List<ComponentStateRuntimeDiagnostic> _completionDiagnostics = [];
    private readonly List<Action> _pendingCompletionEvents = [];
    private readonly object _completionGate = new();
    private ComponentPresentationSnapshot _presentation = ComponentPresentationSnapshot.Empty;
    private bool _dispatching;

    public ComponentStateFrame(
        BoundComponentInstance instance,
        TState initialState,
        Func<TState, ComponentPresentationSnapshot> present,
        RendererAttachmentRegistry attachments)
    {
        Instance = instance;
        State = initialState;
        _present = present;
        _attachments = attachments;
    }

    public BoundComponentInstance Instance { get; }
    public string StateIdentity => Instance.StateIdentity;
    public TState State { get; private set; }
    public bool IsDestroyed { get; private set; }
    public IReadOnlyList<ComponentRuntimeTrace> Trace => _trace;
    public IReadOnlyList<ComponentStateRuntimeDiagnostic> CompletionDiagnostics => _completionDiagnostics;

    /// <summary>Evaluates and mounts the component's initial presentation.</summary>
    public IReadOnlyList<ComponentStateRuntimeDiagnostic> Start()
    {
        if (IsDestroyed)
        {
            return [Destroyed(null)];
        }

        return ReplacePresentation(_present(State), eventName: null);
    }

    /// <summary>
    /// Evaluates one typed component-local event deterministically.  The
    /// reducer is the Copeland transition realization; it cannot receive a
    /// renderer root or mutate an adapter directly.
    /// </summary>
    public ComponentStateDispatchResult<TState> Dispatch<TEvent>(
        string eventName,
        TEvent @event,
        Func<TState, TEvent, TState> transition)
    {
        if (IsDestroyed)
        {
            return new ComponentStateDispatchResult<TState>(State, [Destroyed(eventName)], false);
        }

        return Dispatch(
            eventName,
            @event,
            (state, value) => ComponentTransition<TState>.StateOnly(transition(state, value)));
    }

    /// <summary>
    /// Runs the compiler-owned completion pipeline. State effects start after
    /// the state assignment; presentation effects start after the local
    /// attachment delta has been accepted; attachment effects start only after
    /// every affected adapter operation has returned success or failure.
    /// </summary>
    public ComponentStateDispatchResult<TState> Dispatch<TEvent>(
        string eventName,
        TEvent @event,
        Func<TState, TEvent, ComponentTransition<TState>> transition)
    {
        if (IsDestroyed)
        {
            return new ComponentStateDispatchResult<TState>(State, [Destroyed(eventName)], false);
        }

        ComponentTransition<TState> result;
        try
        {
            result = transition(State, @event);
        }
        catch (Exception exception)
        {
            return new ComponentStateDispatchResult<TState>(
                State,
                [new ComponentStateRuntimeDiagnostic(
                    "COPE-COMPONENT-STATE-0101",
                    Instance.StableIdentity,
                    StateIdentity,
                    eventName,
                    null,
                    "Component transition failed before presentation evaluation: " + exception.Message)],
                false);
        }

        ComponentPresentationSnapshot nextPresentation;
        try
        {
            nextPresentation = _present(result.NextState);
        }
        catch (Exception exception)
        {
            return new ComponentStateDispatchResult<TState>(
                State,
                [new ComponentStateRuntimeDiagnostic(
                    "COPE-COMPONENT-STATE-0102",
                    Instance.StableIdentity,
                    StateIdentity,
                    eventName,
                    null,
                    "Component presentation evaluation failed: " + exception.Message)],
                false);
        }

        // State becomes current before adapter delivery. The presentation was
        // already fully evaluated, so no adapter can observe a half-evaluated
        // state value.
        State = result.NextState;
        _trace.Add(new ComponentRuntimeTrace("StateCommitted", Instance.StableIdentity, eventName, eventName, null, ComponentCompletionPhase.StateCommitted));
        var diagnostics = new List<ComponentStateRuntimeDiagnostic>();
        bool previousDispatching = _dispatching;
        _dispatching = true;
        try
        {
            bool continuePipeline = RunEffects(result.Effects, ComponentCompletionPhase.StateCommitted, eventName, diagnostics);
            if (continuePipeline)
            {
                diagnostics.AddRange(ReplacePresentation(nextPresentation, eventName));
                _trace.Add(new ComponentRuntimeTrace("PresentationCommitted", Instance.StableIdentity, eventName, eventName, null, ComponentCompletionPhase.PresentationCommitted));
                continuePipeline = RunEffects(result.Effects, ComponentCompletionPhase.PresentationCommitted, eventName, diagnostics);
            }

            if (continuePipeline)
            {
                _trace.Add(new ComponentRuntimeTrace("AttachmentsSettled", Instance.StableIdentity, eventName, eventName, null, ComponentCompletionPhase.AttachmentsSettled));
                RunEffects(result.Effects, ComponentCompletionPhase.AttachmentsSettled, eventName, diagnostics);
            }
        }
        finally
        {
            _dispatching = previousDispatching;
        }

        if (!previousDispatching)
        {
            DrainCompletionEvents();
        }

        return new ComponentStateDispatchResult<TState>(State, diagnostics, true);
    }

    /// <summary>
    /// Semantic destruction releases the complete attachment subtree in
    /// deepest-first order and makes future event delivery an explicit error.
    /// </summary>
    public IReadOnlyList<ComponentStateRuntimeDiagnostic> Destroy()
    {
        if (IsDestroyed)
        {
            return [];
        }

        IsDestroyed = true;
        _lifetime.Cancel();
        IReadOnlyList<RendererRuntimeDiagnostic> diagnostics = _attachments.UnmountSubtree(
            _presentation.Attachments
                .Select(item => item.Attachment)
                .FirstOrDefault(item => item.ComponentInstanceId == Instance.StableIdentity)
            ?? HostAttachmentMir.Create(Instance));
        _presentation = ComponentPresentationSnapshot.Empty;
        return diagnostics.Select(diagnostic => FromRenderer(diagnostic, null)).ToArray();
    }

    private bool RunEffects(
        IReadOnlyList<ComponentEffectRequest<TState>> effects,
        ComponentCompletionPhase phase,
        string eventName,
        List<ComponentStateRuntimeDiagnostic> diagnostics)
    {
        ComponentEffectRequest<TState>[] ordered = effects
            .Where(effect => effect.Descriptor.Phase == phase)
            .OrderBy(effect => effect.Descriptor.AuthoredOrder)
            .ThenBy(effect => effect.Descriptor.Identity, StringComparer.Ordinal)
            .ToArray();
        foreach (IGrouping<string, ComponentEffectRequest<TState>> duplicate in ordered.GroupBy(effect => effect.Descriptor.Identity, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            diagnostics.Add(EffectDiagnostic("COPE-COMPONENT-EFFECT-0001", eventName, duplicate.Key, phase, "A transition requested the same effect identity more than once."));
            return false;
        }

        foreach (ComponentEffectRequest<TState> effect in ordered)
        {
            var context = new ComponentEffectContext<TState>(
                Instance.StableIdentity,
                StateIdentity,
                eventName,
                eventName,
                effect.Descriptor,
                State,
                _lifetime.Token);
            _trace.Add(new ComponentRuntimeTrace("EffectStarted", Instance.StableIdentity, eventName, eventName, effect.Descriptor.Identity, phase));
            try
            {
                ValueTask<ComponentEffectCompletion<TState>?> pending = effect.Execute(context);
                if (pending.IsCompletedSuccessfully)
                {
                    HandleEffectCompletion(effect.Descriptor, eventName, pending.Result);
                    continue;
                }

                _ = ObserveEffectAsync(effect.Descriptor, eventName, pending);
            }
            catch (Exception exception)
            {
                diagnostics.Add(EffectDiagnostic("COPE-COMPONENT-EFFECT-0002", eventName, effect.Descriptor.Identity, phase, "Effect failed to start: " + exception.Message));
                _trace.Add(new ComponentRuntimeTrace("EffectFailed", Instance.StableIdentity, eventName, eventName, effect.Descriptor.Identity, phase, exception.Message));
                return false;
            }
        }

        return true;
    }

    private async Task ObserveEffectAsync(
        ComponentEffectDescriptor descriptor,
        string eventName,
        ValueTask<ComponentEffectCompletion<TState>?> pending)
    {
        try
        {
            ComponentEffectCompletion<TState>? completion = await pending.ConfigureAwait(false);
            HandleEffectCompletion(descriptor, eventName, completion);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            _trace.Add(new ComponentRuntimeTrace("EffectCompletionDiscarded", Instance.StableIdentity, eventName, eventName, descriptor.Identity, descriptor.Phase, "Component lifetime ended."));
        }
        catch (Exception exception)
        {
            ComponentStateRuntimeDiagnostic diagnostic = EffectDiagnostic("COPE-COMPONENT-EFFECT-0002", eventName, descriptor.Identity, descriptor.Phase, "Effect failed: " + exception.Message);
            _completionDiagnostics.Add(diagnostic);
            _trace.Add(new ComponentRuntimeTrace("EffectFailed", Instance.StableIdentity, eventName, eventName, descriptor.Identity, descriptor.Phase, exception.Message));
        }
    }

    private void HandleEffectCompletion(
        ComponentEffectDescriptor descriptor,
        string eventName,
        ComponentEffectCompletion<TState>? completion)
    {
        if (IsDestroyed || _lifetime.IsCancellationRequested)
        {
            _completionDiagnostics.Add(EffectDiagnostic("COPE-COMPONENT-EFFECT-0003", eventName, descriptor.Identity, descriptor.Phase, "Effect completion was discarded because its component frame was destroyed."));
            _trace.Add(new ComponentRuntimeTrace("EffectCompletionDiscarded", Instance.StableIdentity, eventName, eventName, descriptor.Identity, descriptor.Phase));
            return;
        }

        _trace.Add(new ComponentRuntimeTrace("EffectCompleted", Instance.StableIdentity, eventName, eventName, descriptor.Identity, descriptor.Phase));
        if (completion is null)
        {
            return;
        }

        void Deliver()
        {
            if (IsDestroyed || _lifetime.IsCancellationRequested)
            {
                _completionDiagnostics.Add(EffectDiagnostic("COPE-COMPONENT-EFFECT-0003", eventName, descriptor.Identity, descriptor.Phase, "Effect completion was discarded because its component frame was destroyed."));
                _trace.Add(new ComponentRuntimeTrace("EffectCompletionDiscarded", Instance.StableIdentity, eventName, eventName, descriptor.Identity, descriptor.Phase));
                return;
            }

            completion.Deliver();
            _trace.Add(new ComponentRuntimeTrace("CompletionEventDispatched", Instance.StableIdentity, completion.EventName, eventName, descriptor.Identity, descriptor.Phase));
        }

        if (_dispatching)
        {
            // The originating transition completes all of its requested phases
            // before a synchronous completion can begin another transition.
            lock (_completionGate)
            {
                _pendingCompletionEvents.Add(Deliver);
            }
        }
        else
        {
            Deliver();
        }
    }

    private void DrainCompletionEvents()
    {
        while (true)
        {
            Action[] pending;
            lock (_completionGate)
            {
                if (_pendingCompletionEvents.Count == 0)
                {
                    return;
                }

                pending = _pendingCompletionEvents.ToArray();
                _pendingCompletionEvents.Clear();
            }

            foreach (Action completion in pending)
            {
                completion();
            }
        }
    }

    private ComponentStateRuntimeDiagnostic EffectDiagnostic(
        string id,
        string eventName,
        string effectIdentity,
        ComponentCompletionPhase phase,
        string message)
        => new(
            id,
            Instance.StableIdentity,
            StateIdentity,
            eventName,
            effectIdentity,
            "Effect '" + effectIdentity + "' at phase '" + phase + "' for component frame '" + Instance.StableIdentity + "': " + message);

    private IReadOnlyList<ComponentStateRuntimeDiagnostic> ReplacePresentation(
        ComponentPresentationSnapshot next,
        string? eventName)
    {
        var oldById = _presentation.Attachments.ToDictionary(item => item.Attachment.AttachmentId, StringComparer.Ordinal);
        var nextById = next.Attachments.ToDictionary(item => item.Attachment.AttachmentId, StringComparer.Ordinal);
        var diagnostics = new List<ComponentStateRuntimeDiagnostic>();

        foreach (ComponentPresentationAttachment removed in _presentation.Attachments
            .Where(item => !nextById.ContainsKey(item.Attachment.AttachmentId))
            .OrderByDescending(item => Depth(item.Attachment, oldById, nextById))
            .ThenBy(item => item.Attachment.AttachmentId, StringComparer.Ordinal))
        {
            Add(_attachments.Unmount(removed.Attachment), eventName);
        }

        foreach (ComponentPresentationAttachment replacement in next.Attachments
            .Where(item => oldById.TryGetValue(item.Attachment.AttachmentId, out ComponentPresentationAttachment? prior)
                && !IsCompatible(prior.Attachment, item.Attachment))
            .OrderByDescending(item => Depth(item.Attachment, oldById, nextById))
            .ThenBy(item => item.Attachment.AttachmentId, StringComparer.Ordinal))
        {
            Add(_attachments.Unmount(oldById[replacement.Attachment.AttachmentId].Attachment), eventName);
            Add(_attachments.Mount(replacement.Attachment, replacement.Payload), eventName);
        }

        foreach (ComponentPresentationAttachment retained in next.Attachments
            .Where(item => oldById.TryGetValue(item.Attachment.AttachmentId, out ComponentPresentationAttachment? prior)
                && IsCompatible(prior.Attachment, item.Attachment)
                && !Equals(prior.Payload, item.Payload))
            .OrderBy(item => Depth(item.Attachment, oldById, nextById))
            .ThenBy(item => item.Attachment.AttachmentId, StringComparer.Ordinal))
        {
            Add(_attachments.Update(retained.Attachment, retained.Payload), eventName);
        }

        foreach (ComponentPresentationAttachment added in next.Attachments
            .Where(item => !oldById.ContainsKey(item.Attachment.AttachmentId))
            .OrderBy(item => Depth(item.Attachment, oldById, nextById))
            .ThenBy(item => item.Attachment.AttachmentId, StringComparer.Ordinal))
        {
            Add(_attachments.Mount(added.Attachment, added.Payload), eventName);
        }

        _presentation = next;
        return diagnostics;

        void Add(RendererRuntimeDiagnostic? diagnostic, string? sourceEvent)
        {
            if (diagnostic is not null)
            {
                diagnostics.Add(FromRenderer(diagnostic, sourceEvent));
            }
        }
    }

    private ComponentStateRuntimeDiagnostic Destroyed(string? eventName)
        => new(
            "COPE-COMPONENT-STATE-0103",
            Instance.StableIdentity,
            StateIdentity,
            eventName,
            null,
            "Event delivery targeted a destroyed component instance.");

    private ComponentStateRuntimeDiagnostic FromRenderer(RendererRuntimeDiagnostic diagnostic, string? eventName)
        => new(
            diagnostic.Id,
            Instance.StableIdentity,
            StateIdentity,
            eventName,
            diagnostic.AttachmentId,
            diagnostic.Message);

    private static bool IsCompatible(HostAttachmentMir oldAttachment, HostAttachmentMir newAttachment)
        => oldAttachment.AdapterId == newAttachment.AdapterId
            && oldAttachment.HostBoxId == newAttachment.HostBoxId
            && oldAttachment.PayloadContract == newAttachment.PayloadContract
            && oldAttachment.RequiredContentCapabilities == newAttachment.RequiredContentCapabilities;

    private static int Depth(
        HostAttachmentMir attachment,
        IReadOnlyDictionary<string, ComponentPresentationAttachment> oldById,
        IReadOnlyDictionary<string, ComponentPresentationAttachment> nextById)
    {
        var byComponent = oldById.Values.Concat(nextById.Values)
            .GroupBy(item => item.Attachment.ComponentInstanceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Attachment, StringComparer.Ordinal);
        int depth = 0;
        string? parent = attachment.ParentComponentInstanceId;
        while (parent is not null && byComponent.TryGetValue(parent, out HostAttachmentMir? parentAttachment))
        {
            depth += 1;
            parent = parentAttachment.ParentComponentInstanceId;
        }

        return depth;
    }
}

/// <summary>
/// A renderer callback holds only this typed bridge. It retains the canonical
/// component-frame identity and has no access to state storage or adapter
/// roots, preventing a global event bus or renderer-owned application state.
/// </summary>
public sealed class ComponentEventBridge<TState, TEvent>
{
    private readonly ComponentStateFrame<TState> _frame;
    private readonly Func<TState, TEvent, ComponentTransition<TState>> _transition;

    public ComponentEventBridge(
        ComponentStateFrame<TState> frame,
        string eventName,
        Func<TState, TEvent, TState> transition)
        : this(frame, eventName, (state, @event) => ComponentTransition<TState>.StateOnly(transition(state, @event)))
    {
    }

    public ComponentEventBridge(
        ComponentStateFrame<TState> frame,
        string eventName,
        Func<TState, TEvent, ComponentTransition<TState>> transition)
    {
        _frame = frame;
        EventName = eventName;
        _transition = transition;
    }

    public string ComponentInstanceId => _frame.Instance.StableIdentity;
    public string EventName { get; }

    public ComponentStateDispatchResult<TState> Deliver(TEvent @event)
        => _frame.Dispatch(EventName, @event, _transition);
}
