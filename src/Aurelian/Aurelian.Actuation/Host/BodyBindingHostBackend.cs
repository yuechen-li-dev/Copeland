using System.Runtime.CompilerServices;

namespace Aurelian.Actuation.Host;

/// <summary>
/// Owns the semantic agent/body table and lowers its commands through the
/// existing host backend. Backend actor identity is contained at this seam.
/// </summary>
public sealed class BodyBindingHostBackend : IHostPresenterBackend
{
    private readonly IHostPresenterBackend inner;
    private readonly BodyBindingRegistry registry;
    private readonly Dictionary<BodyId, HostActorObservation> materializations = new();
    private readonly Dictionary<BodyId, HostActorId> candidateIdentities = new();
    private readonly Queue<HostRuntimeObservation> observations = new();
    private ulong nextRuntimeSequence;

    public BodyBindingHostBackend(
        IHostPresenterBackend inner,
        BodyBindingRegistry? registry = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.registry = registry ?? new BodyBindingRegistry();
    }

    public BodyBindingRegistry Registry => registry;

    public void RegisterCandidate(BodyId body, HostActorId backendIdentity)
    {
        if (!backendIdentity.IsValid)
        {
            throw new ArgumentException("Backend body identity must be valid.", nameof(backendIdentity));
        }

        candidateIdentities[body] = backendIdentity;
    }

    public void ObserveMaterialization(BodyId body, HostActorObservation actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        candidateIdentities[body] = actor.ActorId;
        materializations[body] = actor;
        nextRuntimeSequence = Math.Max(nextRuntimeSequence, actor.Sequence);
    }

    public async ValueTask<HostCommandReceipt> SubmitAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        HostCommandValidationResult validation = request.Validate();
        if (!validation.IsValid)
        {
            return Reject(request, validation.FailureReason!);
        }

        return request.Kind switch
        {
            HostCommandKind.BindBody => await BindAsync(
                request,
                (BindBodyArguments)request.Arguments,
                cancellationToken).ConfigureAwait(false),
            HostCommandKind.ReleaseBody => await ReleaseAsync(
                request,
                (ReleaseBodyArguments)request.Arguments,
                cancellationToken).ConfigureAwait(false),
            HostCommandKind.QueryBodyBinding => Query(
                request,
                (QueryBodyBindingArguments)request.Arguments),
            HostCommandKind.MoveBodyToward => await MoveAsync(
                request,
                (MoveBodyTowardArguments)request.Arguments,
                cancellationToken).ConfigureAwait(false),
            _ => Reject(request, "body_command_required"),
        };
    }

    public async IAsyncEnumerable<HostRuntimeObservation> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (observations.TryDequeue(out HostRuntimeObservation? observation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return observation;
            await Task.Yield();
        }
    }

    private async ValueTask<HostCommandReceipt> BindAsync(
        HostCommandRequest request,
        BindBodyArguments arguments,
        CancellationToken cancellationToken)
    {
        if (!candidateIdentities.TryGetValue(arguments.Body, out HostActorId candidate))
        {
            return Reject(request, "body_materialization_unknown");
        }

        if (candidate.Generation != arguments.ExpectedBodyGeneration)
        {
            return Reject(request, "stale_body_generation");
        }

        if (materializations.TryGetValue(arguments.Body, out HostActorObservation? observed)
            && !observed.Loaded)
        {
            return Reject(request, "body_unloaded");
        }

        BodyBindingRegistryResult beginning = registry.BeginBinding(
            arguments.Agent,
            arguments.Body,
            arguments.Kind,
            arguments.ExpectedBodyGeneration);
        if (!beginning.Accepted)
        {
            return Reject(request, beginning.FailureReason!);
        }

        HostActionResult lowered = await HostActionRunner.ExecuteAsync(
            inner,
            request with
            {
                Kind = HostCommandKind.BeginHostSession,
                Arguments = EmptyHostCommandArguments.Instance,
            },
            cancellationToken).ConfigureAwait(false);

        BodyBindingRegistryResult finalBinding;
        if (lowered.State == HostActionState.Completed && lowered.Observation is not null)
        {
            ObserveMaterialization(arguments.Body, lowered.Observation);
            finalBinding = registry.CompleteBinding(
                arguments.Agent,
                arguments.Body,
                lowered.Observation.ActorId.Generation);
        }
        else
        {
            bool uncertain = lowered.State is HostActionState.TimedOut or HostActionState.Interrupted;
            finalBinding = registry.FailBinding(arguments.Agent, arguments.Body, uncertain);
        }

        Enqueue(request, lowered, arguments.Body, finalBinding.Binding);
        return Accept(request);
    }

    private async ValueTask<HostCommandReceipt> MoveAsync(
        HostCommandRequest request,
        MoveBodyTowardArguments arguments,
        CancellationToken cancellationToken)
    {
        BodyBindingRegistryResult authorization = registry.AuthorizeExclusiveCommand(
            arguments.Agent,
            arguments.Body,
            arguments.ExpectedBodyGeneration);
        if (!authorization.Accepted)
        {
            return Reject(request, authorization.FailureReason!);
        }

        if (!materializations.TryGetValue(arguments.Body, out HostActorObservation? actor))
        {
            return Reject(request, "body_observation_missing");
        }

        if (!actor.Loaded)
        {
            BodyBindingRegistryResult lost = registry.MarkLost(
                arguments.Agent,
                arguments.Body,
                arguments.ExpectedBodyGeneration);
            EnqueueFailure(request, HostActionState.ActorUnloaded, "body_unloaded", arguments.Body, lost.Binding);
            return Accept(request);
        }

        HostActionResult lowered = await HostActionRunner.ExecuteAsync(
            inner,
            request with
            {
                Kind = HostCommandKind.MoveToward,
                Arguments = new MoveTowardArguments(
                    actor.ActorId,
                    arguments.TargetPosition,
                    arguments.StoppingDistance,
                    arguments.MaximumDistance,
                    arguments.SpeedPolicy,
                    arguments.ExpectedObservationSequence),
            },
            cancellationToken).ConfigureAwait(false);

        if (lowered.Observation is not null)
        {
            ObserveMaterialization(arguments.Body, lowered.Observation);
        }

        BodyBinding binding = authorization.Binding!;
        if (lowered.State is HostActionState.ActorUnloaded or HostActionState.TargetInvalid)
        {
            BodyBindingRegistryResult lost = registry.MarkLost(
                arguments.Agent,
                arguments.Body,
                arguments.ExpectedBodyGeneration);
            binding = lost.Binding ?? binding;
        }

        Enqueue(request, lowered, arguments.Body, binding);
        return Accept(request);
    }

    private async ValueTask<HostCommandReceipt> ReleaseAsync(
        HostCommandRequest request,
        ReleaseBodyArguments arguments,
        CancellationToken cancellationToken)
    {
        BodyBindingRegistryResult releasing = registry.BeginRelease(
            arguments.Agent,
            arguments.Body,
            arguments.ExpectedBodyGeneration);
        if (!releasing.Accepted)
        {
            return Reject(request, releasing.FailureReason!);
        }

        if (releasing.Binding!.State == BodyBindingState.Released)
        {
            EnqueueFailure(request, HostActionState.Completed, null, arguments.Body, releasing.Binding);
            return Accept(request);
        }

        HostActionResult lowered = await HostActionRunner.ExecuteAsync(
            inner,
            request with
            {
                Kind = HostCommandKind.EndHostSession,
                Arguments = EmptyHostCommandArguments.Instance,
            },
            cancellationToken).ConfigureAwait(false);

        BodyBindingRegistryResult finalBinding = lowered.State == HostActionState.Completed
            ? registry.CompleteRelease(arguments.Agent, arguments.Body, arguments.ExpectedBodyGeneration)
            : registry.FailBinding(
                arguments.Agent,
                arguments.Body,
                restoreRequired: true);
        Enqueue(request, lowered, arguments.Body, finalBinding.Binding);
        return Accept(request);
    }

    private HostCommandReceipt Query(
        HostCommandRequest request,
        QueryBodyBindingArguments arguments)
    {
        BodyBinding? binding = registry.Query(arguments.Agent);
        if (binding is null || binding.Body != arguments.Body)
        {
            return Reject(request, "binding_not_found");
        }

        EnqueueFailure(request, HostActionState.Completed, null, arguments.Body, binding);
        return Accept(request);
    }

    private void Enqueue(
        HostCommandRequest request,
        HostActionResult lowered,
        BodyId bodyId,
        BodyBinding? binding)
    {
        BodyObservation? body = ToBodyObservation(bodyId, binding);
        BodyBindingObservation? bindingObservation = binding is null
            ? null
            : new BodyBindingObservation(binding, body, lowered.FailureReason);
        var bodyResult = new BodyCommandResult(
            request.RequestId,
            lowered.State,
            lowered.FailureReason,
            body,
            bindingObservation);
        var action = new HostActionResult(
            request.RequestId,
            lowered.State,
            lowered.FailureReason,
            lowered.Observation,
            bodyResult);
        observations.Enqueue(new HostRuntimeObservation(
            ++nextRuntimeSequence,
            ActiveHost: null,
            new PlayerAnchorObservation(0, 0, 0, 0),
            new CameraObservation(0, HostCameraMode.Unknown),
            new CrosshairObservation(0),
            new MovementObservation(false, false),
            action,
            lowered.Observation,
            bindingObservation,
            body));
    }

    private void EnqueueFailure(
        HostCommandRequest request,
        HostActionState state,
        string? reason,
        BodyId bodyId,
        BodyBinding? binding)
    {
        Enqueue(
            request,
            new HostActionResult(request.RequestId, state, reason),
            bodyId,
            binding);
    }

    private BodyObservation? ToBodyObservation(BodyId bodyId, BodyBinding? binding)
    {
        if (!materializations.TryGetValue(bodyId, out HostActorObservation? actor))
        {
            return null;
        }

        return new BodyObservation(
            bodyId,
            actor.Loaded,
            actor.LifeState == HostActorLifeState.Alive,
            actor.Position,
            new BodyCapabilities(
                actor.Capabilities.CanMoveToward,
                CanLook: false,
                actor.Capabilities.AnimatedLocomotion != HostCapabilitySupport.Unsupported,
                CanReceiveInput: false,
                CanBeExclusiveBound: true,
                CanRestore: true),
            binding?.State ?? BodyBindingState.Unbound,
            binding?.Agent,
            binding?.Generation ?? actor.ActorId.Generation,
            actor.Sequence);
    }

    private HostCommandReceipt Accept(HostCommandRequest request)
    {
        return new HostCommandReceipt(request.RequestId, true, nextRuntimeSequence, null);
    }

    private HostCommandReceipt Reject(HostCommandRequest request, string reason)
    {
        return new HostCommandReceipt(request.RequestId, false, nextRuntimeSequence, reason);
    }
}
