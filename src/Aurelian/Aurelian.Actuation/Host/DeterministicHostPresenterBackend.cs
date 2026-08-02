using System.Runtime.CompilerServices;

namespace Aurelian.Actuation.Host;

/// <summary>
/// Deterministic value-only backend for host-independent agent tests. One
/// MoveToward request advances by at most the command's bounded distance.
/// </summary>
public sealed class DeterministicHostPresenterBackend : IHostPresenterBackend
{
    private readonly Queue<HostRuntimeObservation> observations = new();
    private HostActorObservation actor;

    public DeterministicHostPresenterBackend(HostActorObservation initialActor)
    {
        actor = initialActor ?? throw new ArgumentNullException(nameof(initialActor));
    }

    public HostActionState? InjectedTerminalState { get; set; }

    public HostActorObservation CurrentActor => actor;

    public int SubmittedCommandCount { get; private set; }

    public ValueTask<HostCommandReceipt> SubmitAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SubmittedCommandCount++;
        HostCommandValidationResult validation = request.Validate();
        if (!validation.IsValid)
        {
            return ValueTask.FromResult(Reject(request, validation.FailureReason!));
        }

        if (request.Arguments is not MoveTowardArguments move)
        {
            return ValueTask.FromResult(Reject(request, "unsupported_command"));
        }

        if (move.ActorId != actor.ActorId)
        {
            return ValueTask.FromResult(Reject(request, "actor_generation_mismatch"));
        }

        if (move.ExpectedObservationSequence != actor.Sequence)
        {
            return ValueTask.FromResult(Reject(request, "stale_observation_sequence"));
        }

        if (!actor.Loaded)
        {
            return ValueTask.FromResult(AcceptTerminal(request, HostActionState.ActorUnloaded, "actor_unloaded"));
        }

        if (!actor.Capabilities.CanMoveToward)
        {
            return ValueTask.FromResult(AcceptTerminal(request, HostActionState.Unsupported, "movement_unsupported"));
        }

        Enqueue(request.RequestId, HostActionState.Accepted, null, actor.Position);
        Enqueue(request.RequestId, HostActionState.Running, null, actor.Position);

        HostActionState terminal = InjectedTerminalState ?? HostActionState.Completed;
        HostPosition3 finalPosition = terminal == HostActionState.Completed
            ? Advance(actor.Position, move)
            : actor.Position;
        string? reason = terminal == HostActionState.Completed
            ? null
            : ToFailureReason(terminal);
        Enqueue(request.RequestId, terminal, reason, finalPosition);

        return ValueTask.FromResult(new HostCommandReceipt(
            request.RequestId,
            Accepted: true,
            actor.Sequence,
            null));
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

    private HostCommandReceipt Reject(HostCommandRequest request, string reason)
    {
        Enqueue(request.RequestId, HostActionState.Rejected, reason, actor.Position);
        return new HostCommandReceipt(request.RequestId, Accepted: false, actor.Sequence, reason);
    }

    private HostCommandReceipt AcceptTerminal(
        HostCommandRequest request,
        HostActionState state,
        string reason)
    {
        Enqueue(request.RequestId, HostActionState.Accepted, null, actor.Position);
        Enqueue(request.RequestId, state, reason, actor.Position);
        return new HostCommandReceipt(request.RequestId, Accepted: true, actor.Sequence, null);
    }

    private void Enqueue(
        Guid requestId,
        HostActionState state,
        string? failureReason,
        HostPosition3 position)
    {
        ulong sequence = actor.Sequence + 1;
        actor = actor with
        {
            Position = position,
            MovementState = state == HostActionState.Running
                ? HostActorMovementState.Moving
                : HostActorMovementState.Idle,
            ActionState = state,
            Sequence = sequence,
        };

        HostActionResult action = new(requestId, state, failureReason, actor);
        observations.Enqueue(new HostRuntimeObservation(
            sequence,
            ActiveHost: null,
            new PlayerAnchorObservation(0x14, 0.0f, 0.0f, 0.0f),
            new CameraObservation(actor.ActorId.FormId, HostCameraMode.ThirdPerson),
            new CrosshairObservation(0),
            new MovementObservation(ControllerObserved: false, state == HostActionState.Running),
            action,
            actor));
    }

    private static HostPosition3 Advance(HostPosition3 current, MoveTowardArguments move)
    {
        float distance = current.DistanceTo(move.TargetPosition);
        float travel = MathF.Min(move.MaximumDistance, MathF.Max(0.0f, distance - move.StoppingDistance));
        if (travel <= 0.0f || distance <= 0.0001f)
        {
            return current;
        }

        float scale = travel / distance;
        return new HostPosition3(
            current.X + ((move.TargetPosition.X - current.X) * scale),
            current.Y + ((move.TargetPosition.Y - current.Y) * scale),
            current.Z + ((move.TargetPosition.Z - current.Z) * scale));
    }

    private static string ToFailureReason(HostActionState state) => state switch
    {
        HostActionState.Blocked => "movement_blocked",
        HostActionState.Interrupted => "movement_interrupted",
        HostActionState.TimedOut => "movement_timed_out",
        HostActionState.TargetInvalid => "target_invalid",
        HostActionState.ActorUnloaded => "actor_unloaded",
        HostActionState.Unsupported => "movement_unsupported",
        HostActionState.EngineRefused => "engine_refused",
        _ => "movement_failed",
    };
}

public sealed class ReplayHostPresenterBackend : IHostPresenterBackend
{
    private readonly IReadOnlyList<HostRuntimeObservation> observations;

    public ReplayHostPresenterBackend(IReadOnlyList<HostRuntimeObservation> observations)
    {
        this.observations = observations?.ToArray()
            ?? throw new ArgumentNullException(nameof(observations));
    }

    public ValueTask<HostCommandReceipt> SubmitAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HostCommandValidationResult validation = request.Validate();
        if (!validation.IsValid)
        {
            return ValueTask.FromResult(new HostCommandReceipt(
                request.RequestId,
                Accepted: false,
                RuntimeSequence: 0,
                validation.FailureReason));
        }

        HostRuntimeObservation? accepted = observations.FirstOrDefault(
            item => item.Action?.RequestId == request.RequestId
                && item.Action.State == HostActionState.Accepted);
        return ValueTask.FromResult(new HostCommandReceipt(
            request.RequestId,
            accepted is not null,
            accepted?.RuntimeSequence ?? 0,
            accepted is null ? "replay_request_not_found" : null));
    }

    public async IAsyncEnumerable<HostRuntimeObservation> ObserveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (HostRuntimeObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return observation;
            await Task.Yield();
        }
    }
}

public static class HostActionRunner
{
    public static async ValueTask<HostActionResult> ExecuteAsync(
        IHostPresenterBackend backend,
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            HostCommandReceipt receipt = await backend.SubmitAsync(request, timeout.Token).ConfigureAwait(false);
            if (!receipt.Accepted)
            {
                return new HostActionResult(request.RequestId, HostActionState.Rejected, receipt.FailureReason);
            }

            await foreach (HostRuntimeObservation observation in backend.ObserveAsync(timeout.Token).ConfigureAwait(false))
            {
                if (observation.Action is { } action
                    && action.RequestId == request.RequestId
                    && action.IsTerminal)
                {
                    return action;
                }
            }

            return new HostActionResult(request.RequestId, HostActionState.Failed, "completion_not_observed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HostActionResult(request.RequestId, HostActionState.TimedOut, "action_timed_out");
        }
        catch (OperationCanceledException)
        {
            return new HostActionResult(request.RequestId, HostActionState.Interrupted, "action_cancelled");
        }
    }
}
