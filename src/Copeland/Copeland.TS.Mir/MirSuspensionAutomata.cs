namespace Copeland.TS.Mir;

/// <summary>
/// Compiler-local lower representation for an async function after structured
/// MIR has been split at real suspension boundaries. It intentionally models a
/// generated continuation, not a user-authored state-machine language.
/// </summary>
public sealed class MirSuspensionAutomaton(
    string identity,
    string ownerFunctionName,
    MirAutomatonStateId entryStateId,
    IReadOnlyList<MirFrameSlot> frameSlots,
    IReadOnlyList<MirAutomatonState> states,
    IReadOnlyList<MirAutomatonTransition> transitions)
{
    public string Identity { get; } = identity;
    public string OwnerFunctionName { get; } = ownerFunctionName;
    public MirAutomatonStateId EntryStateId { get; } = entryStateId;
    public IReadOnlyList<MirFrameSlot> FrameSlots { get; } = Array.AsReadOnly(frameSlots.ToArray());
    public IReadOnlyList<MirAutomatonState> States { get; } = Array.AsReadOnly(states.ToArray());
    public IReadOnlyList<MirAutomatonTransition> Transitions { get; } = Array.AsReadOnly(transitions.ToArray());
}

public static class MirSuspensionAutomatonLimits
{
    public const int MaximumStates = 256;
    public const int MaximumTransitions = 512;
    public const int MaximumSuspensionPoints = 128;
    public const int MaximumFrameSlots = 256;
    public const int MaximumStructuredNesting = 32;
    public const int MaximumWorklistSteps = 8_192;
}

public readonly record struct MirAutomatonStateId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct MirFrameSlotId(string Value)
{
    public override string ToString() => Value;
}

public sealed class MirFrameSlot(MirFrameSlotId id, MirType type, string provenance, bool isReadOnly = false)
{
    public MirFrameSlotId Id { get; } = id;
    public MirType Type { get; } = type;
    public string Provenance { get; } = provenance;
    public bool IsReadOnly { get; } = isReadOnly;
}

public enum MirAutomatonStateKind
{
    Entry,
    Execution,
    Suspension,
    Completion,
    Cancelled,
    InvariantFailure,
}

public abstract class MirAutomatonState(MirAutomatonStateId id, MirAutomatonStateKind kind, string provenance)
{
    public MirAutomatonStateId Id { get; } = id;
    public MirAutomatonStateKind Kind { get; } = kind;
    public string Provenance { get; } = provenance;
}

public sealed class MirExecutionAutomatonState(
    MirAutomatonStateId id,
    string provenance,
    IReadOnlyList<MirFrameSlotId> reads,
    IReadOnlyList<MirFrameSlotId> writes)
    : MirAutomatonState(id, MirAutomatonStateKind.Execution, provenance)
{
    public IReadOnlyList<MirFrameSlotId> Reads { get; } = Array.AsReadOnly(reads.ToArray());
    public IReadOnlyList<MirFrameSlotId> Writes { get; } = Array.AsReadOnly(writes.ToArray());
}

public sealed class MirAwaitSuspensionAutomatonState(
    MirAutomatonStateId id,
    string provenance,
    MirFrameSlotId awaitedComputationSlot,
    MirType resumeValueType)
    : MirAutomatonState(id, MirAutomatonStateKind.Suspension, provenance)
{
    public MirFrameSlotId AwaitedComputationSlot { get; } = awaitedComputationSlot;
    public MirType ResumeValueType { get; } = resumeValueType;
}

public sealed class MirCompletionAutomatonState(MirAutomatonStateId id, string provenance, MirType completedValueType)
    : MirAutomatonState(id, MirAutomatonStateKind.Completion, provenance)
{
    public MirType CompletedValueType { get; } = completedValueType;
}

public sealed class MirTerminalAutomatonState(MirAutomatonStateId id, MirAutomatonStateKind kind, string provenance)
    : MirAutomatonState(id, kind, provenance)
{
}

public enum MirAutomatonTransitionKind
{
    Unconditional,
    ResumeSuccess,
    ResultError,
    Cancellation,
}

public sealed class MirAutomatonTransition(
    MirAutomatonStateId source,
    MirAutomatonStateId target,
    MirAutomatonTransitionKind kind,
    string provenance)
{
    public MirAutomatonStateId Source { get; } = source;
    public MirAutomatonStateId Target { get; } = target;
    public MirAutomatonTransitionKind Kind { get; } = kind;
    public string Provenance { get; } = provenance;
}

/// <summary>
/// Shared pre-emission verification. Backends must consume only a validated
/// automaton; malformed generated control flow is reported as malformed MIR,
/// never as a C# or JavaScript emitter failure.
/// </summary>
public static class MirSuspensionAutomatonValidator
{
    private const string Prefix = "Malformed MIR suspension automaton";

    public static void Validate(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (MirFunction function in program.Functions)
        {
            if (function.IsAsync && function.SuspensionAutomaton is null)
            {
                Add(diagnostics, $"async function '{function.Name}' has no automaton.");
                continue;
            }

            if (!function.IsAsync && function.SuspensionAutomaton is not null)
            {
                Add(diagnostics, $"synchronous function '{function.Name}' references async-only state.");
                continue;
            }

            if (function.SuspensionAutomaton is not null)
            {
                ValidateAutomaton(function, function.SuspensionAutomaton, diagnostics);
            }
        }
    }

    private static void ValidateAutomaton(
        MirFunction function,
        MirSuspensionAutomaton automaton,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(automaton.Identity))
        {
            Add(diagnostics, $"function '{function.Name}' has a blank automaton identity.");
        }

        if (!string.Equals(function.Name, automaton.OwnerFunctionName, StringComparison.Ordinal))
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' is not owned by async function '{function.Name}'.");
        }

        CheckLimit(automaton.States.Count, MirSuspensionAutomatonLimits.MaximumStates, "states", automaton, diagnostics);
        CheckLimit(automaton.Transitions.Count, MirSuspensionAutomatonLimits.MaximumTransitions, "transitions", automaton, diagnostics);
        CheckLimit(automaton.FrameSlots.Count, MirSuspensionAutomatonLimits.MaximumFrameSlots, "frame slots", automaton, diagnostics);

        var slots = new Dictionary<MirFrameSlotId, MirFrameSlot>();
        foreach (MirFrameSlot slot in automaton.FrameSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id.Value) || !slots.TryAdd(slot.Id, slot))
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' has a blank or duplicate frame slot identity '{slot.Id}'.");
            }
            if (slot.Type is null || string.IsNullOrWhiteSpace(slot.Provenance))
            {
                Add(diagnostics, $"frame slot '{slot.Id}' has an invalid type or provenance.");
            }
        }

        var states = new Dictionary<MirAutomatonStateId, MirAutomatonState>();
        foreach (MirAutomatonState state in automaton.States)
        {
            if (string.IsNullOrWhiteSpace(state.Id.Value) || !states.TryAdd(state.Id, state))
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' has a blank or duplicate state identity '{state.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(state.Provenance))
            {
                Add(diagnostics, $"state '{state.Id}' has no provenance.");
            }
        }

        if (!states.TryGetValue(automaton.EntryStateId, out MirAutomatonState? entry))
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' has a missing entry state '{automaton.EntryStateId}'.");
            return;
        }
        if (entry.Kind != MirAutomatonStateKind.Entry)
        {
            Add(diagnostics, $"entry state '{entry.Id}' does not have Entry kind.");
        }
        if (states.Values.Count(state => state.Kind == MirAutomatonStateKind.Entry) != 1)
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' must have exactly one Entry state.");
        }

        int suspensionCount = 0;
        foreach (MirAutomatonState state in states.Values)
        {
            switch (state)
            {
                case MirExecutionAutomatonState execution:
                    ValidateSlots(execution.Reads, slots, state, "read", diagnostics);
                    ValidateSlots(execution.Writes, slots, state, "write", diagnostics);
                    break;
                case MirAwaitSuspensionAutomatonState suspension:
                    suspensionCount++;
                    if (!slots.TryGetValue(suspension.AwaitedComputationSlot, out MirFrameSlot? awaitedSlot))
                    {
                        Add(diagnostics, $"suspension state '{state.Id}' has no awaited computation slot.");
                    }
                    else if (awaitedSlot.Type is not MirAsyncType asyncType)
                    {
                        Add(diagnostics, $"suspension state '{state.Id}' awaits a non-Async frame slot.");
                    }
                    else if (!MirTypeFacts.AreEquivalent(asyncType.EventualType, suspension.ResumeValueType))
                    {
                        Add(diagnostics, $"suspension state '{state.Id}' resume type does not match its awaited Async value.");
                    }
                    break;
                case MirCompletionAutomatonState completion:
                    if (!MirTypeFacts.AreEquivalent(completion.CompletedValueType, function.ReturnType))
                    {
                        Add(diagnostics, $"completion state '{state.Id}' does not match function '{function.Name}' eventual return type.");
                    }
                    break;
                case MirTerminalAutomatonState terminal when terminal.Kind is not MirAutomatonStateKind.Entry and not MirAutomatonStateKind.Cancelled and not MirAutomatonStateKind.InvariantFailure:
                    Add(diagnostics, $"state '{state.Id}' has illegal terminal kind '{terminal.Kind}'.");
                    break;
            }
        }
        CheckLimit(suspensionCount, MirSuspensionAutomatonLimits.MaximumSuspensionPoints, "suspension points", automaton, diagnostics);

        var outgoing = states.Keys.ToDictionary(id => id, _ => new List<MirAutomatonTransition>());
        foreach (MirAutomatonTransition transition in automaton.Transitions)
        {
            bool knownSource = outgoing.TryGetValue(transition.Source, out List<MirAutomatonTransition>? sourceTransitions);
            if (!knownSource || !states.ContainsKey(transition.Target))
            {
                Add(diagnostics, $"transition '{transition.Source}' -> '{transition.Target}' has an unknown state target or source.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(transition.Provenance))
            {
                Add(diagnostics, $"transition '{transition.Source}' -> '{transition.Target}' has no provenance.");
            }
            if (transition.Kind == MirAutomatonTransitionKind.Cancellation && states[transition.Target].Kind != MirAutomatonStateKind.Cancelled)
            {
                Add(diagnostics, $"cancellation transition from '{transition.Source}' does not target a Cancelled state.");
            }
            if (transition.Kind == MirAutomatonTransitionKind.ResumeSuccess && states[transition.Source] is not MirAwaitSuspensionAutomatonState)
            {
                Add(diagnostics, $"resume-success transition from '{transition.Source}' does not leave a suspension state.");
            }
            sourceTransitions!.Add(transition);
        }

        foreach (MirAutomatonState state in states.Values)
        {
            bool terminal = state.Kind is MirAutomatonStateKind.Completion or MirAutomatonStateKind.Cancelled or MirAutomatonStateKind.InvariantFailure;
            if (!terminal && outgoing[state.Id].Count == 0)
            {
                Add(diagnostics, $"nonterminal state '{state.Id}' has no transition, suspension, or completion.");
            }
            if (state is MirAwaitSuspensionAutomatonState && !outgoing[state.Id].Any(edge => edge.Kind == MirAutomatonTransitionKind.ResumeSuccess))
            {
                Add(diagnostics, $"suspension state '{state.Id}' has no resume-success transition.");
            }
        }

        ValidateReachability(automaton, states, outgoing, diagnostics);
    }

    private static void ValidateSlots(
        IReadOnlyList<MirFrameSlotId> slotIds,
        IReadOnlyDictionary<MirFrameSlotId, MirFrameSlot> slots,
        MirAutomatonState state,
        string operation,
        List<MirValidationDiagnostic> diagnostics)
    {
        foreach (MirFrameSlotId slotId in slotIds)
        {
            if (!slots.ContainsKey(slotId))
            {
                Add(diagnostics, $"state '{state.Id}' attempts to {operation} unknown frame slot '{slotId}'.");
            }
        }
    }

    private static void ValidateReachability(
        MirSuspensionAutomaton automaton,
        IReadOnlyDictionary<MirAutomatonStateId, MirAutomatonState> states,
        IReadOnlyDictionary<MirAutomatonStateId, List<MirAutomatonTransition>> outgoing,
        List<MirValidationDiagnostic> diagnostics)
    {
        var reachable = new HashSet<MirAutomatonStateId>();
        var pending = new Queue<MirAutomatonStateId>();
        pending.Enqueue(automaton.EntryStateId);
        int steps = 0;
        while (pending.Count > 0)
        {
            if (++steps > MirSuspensionAutomatonLimits.MaximumWorklistSteps)
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' exceeds the {MirSuspensionAutomatonLimits.MaximumWorklistSteps} worklist-step bound.");
                return;
            }
            MirAutomatonStateId stateId = pending.Dequeue();
            if (!reachable.Add(stateId)) continue;
            foreach (MirAutomatonTransition edge in outgoing[stateId]) pending.Enqueue(edge.Target);
        }
        foreach (MirAutomatonState state in states.Values)
        {
            if (!reachable.Contains(state.Id))
            {
                Add(diagnostics, $"automaton '{automaton.Identity}' contains unreachable generated state '{state.Id}'.");
            }
        }
    }

    private static void CheckLimit(int actual, int maximum, string noun, MirSuspensionAutomaton automaton, List<MirValidationDiagnostic> diagnostics)
    {
        if (actual > maximum)
        {
            Add(diagnostics, $"automaton '{automaton.Identity}' has {actual} {noun}; maximum is {maximum}.");
        }
    }

    private static void Add(List<MirValidationDiagnostic> diagnostics, string message)
        => diagnostics.Add(new MirValidationDiagnostic($"{Prefix}: {message}"));
}
