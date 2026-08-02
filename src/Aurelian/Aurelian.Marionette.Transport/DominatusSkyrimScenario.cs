using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aurelian.Actuation.Host;

namespace Aurelian.Marionette.Transport;

public sealed record DominatusSkyrimReport(
    int ProtocolVersion,
    string SessionId,
    uint ActorFormId,
    ulong ActorGeneration,
    HostPosition3 Target,
    HostPosition3 InitialPosition,
    HostPosition3 FinalPosition,
    float DistanceBefore,
    float DistanceAfter,
    string SelectedUtilityOption,
    string[] UtilityScores,
    string SemanticCommand,
    Guid CommandRequestId,
    string[] ActionLifecycle,
    ulong InitialObservationSequence,
    ulong FinalObservationSequence,
    ulong ActionRuntimeSequence,
    long ActionDurationMilliseconds,
    string MovementMechanism,
    float PlayerDisplacement,
    uint CameraTargetDuringAction,
    bool SkyrimForegroundAtDecision,
    bool SkyrimForegroundAtActuation,
    string DominatusTransition,
    bool RestoreCompleted,
    bool SessionCleared,
    uint RestoredPlayerFormId,
    uint RestoredCameraTargetFormId);

public sealed partial class MarionetteTransportClient
{
    public async ValueTask<DominatusSkyrimReport> RunDominatusSkyrimScenarioAsync(
        CancellationToken cancellationToken)
    {
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            _config.PipeName,
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        ServerHello hello = await AuthenticateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!hello.Capabilities.Contains("move_toward", StringComparer.Ordinal))
        {
            throw new InvalidDataException("move_toward_capability_missing");
        }

        SkyrimStateResult initial = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!initial.PlayerAvailable || initial.PlayerFormId != 0x14 || initial.ActiveHostSession)
        {
            throw new InvalidDataException("fixture_not_ready_or_host_session_active");
        }

        DeterministicHostFixtureReport fixture = await QueryDeterministicHostFixtureAsync(
            pipe,
            hello.SessionId!,
            cancellationToken).ConfigureAwait(false);
        EvaluateHostRequestResult evaluate = await SendEvaluateAsync(
            pipe,
            new EvaluateHostRequestRequest(
                MarionetteWireProtocol.Version,
                "evaluate_host_request",
                Guid.NewGuid().ToString("N"),
                fixture.SelectedHostFormId,
                2000),
            cancellationToken).ConfigureAwait(false);
        if (evaluate.Status != "completed" || !evaluate.Eligible
            || !evaluate.PendingRequestGeneration.HasValue)
        {
            throw new InvalidDataException($"host_request_failed:{evaluate.FailureReason ?? evaluate.EligibilityReason}");
        }

        SkyrimStateResult pending = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
        HostMutationResult begin = await SendMutationAsync(
            pipe,
            new BeginHostSessionRequest(
                MarionetteWireProtocol.Version,
                "begin_host_session",
                Guid.NewGuid().ToString("N"),
                pending.PendingRequestGeneration ?? 0,
                pending.PendingTargetFormId ?? 0,
                2000),
            cancellationToken).ConfigureAwait(false);
        if (begin.Status != "completed" || begin.HostFormId != fixture.SelectedHostFormId
            || begin.PlayerFormId != 0x14 || begin.CameraTargetFormId != begin.HostFormId)
        {
            throw new InvalidDataException($"begin_host_session_failed:{begin.FailureReason}");
        }

        HostMutationResult? restore = null;
        try
        {
            SkyrimStateResult active = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
            HostActorObservation observation = ToActorObservation(active);
            HostPosition3 target = observation.Position with { Y = observation.Position.Y + 64.0f };
            var definition = SkyrimAgent.Define(
                "skyrim-approach-spike",
                new SkyrimActorBinding(observation.ActorId.FormId, observation.ActorId.Generation),
                new ReachTargetGoal(target, StoppingDistance: 16.0f),
                new ApproachTargetOption(
                    MaximumDistance: 64.0f,
                    HostMovementSpeedPolicy.Walk,
                    MaximumRetries: 1));
            Guid commandRequestId = Guid.NewGuid();
            var backend = new ConnectedMarionetteHostBackend(pipe, observation);
            SkyrimApproachAgentRuntime runtime = definition.CreateRuntime(
                backend,
                observation,
                commandRequestId);

            bool foregroundAtDecision = IsSkyrimForeground();
            var stopwatch = Stopwatch.StartNew();
            SkyrimApproachTransition transition = runtime.RunUntilTerminal();
            stopwatch.Stop();
            bool foregroundAtActuation = IsSkyrimForeground();
            HostActorObservation final = backend.CurrentActor;
            HostMutationResult movement = backend.MovementResult
                ?? throw new InvalidDataException("movement_result_missing");
            if (transition != SkyrimApproachTransition.Completed
                || movement.ActionState != "completed"
                || final.Position.DistanceTo(target) > 16.5f)
            {
                throw new InvalidDataException(
                    $"dominatus_transition_failed:{transition}:{movement.ActionState}");
            }

            restore = await SendMutationAsync(
                pipe,
                new RestoreHostSessionRequest(
                    MarionetteWireProtocol.Version,
                    "restore_host_session",
                    Guid.NewGuid().ToString("N"),
                    begin.HostGeneration,
                    2000),
                cancellationToken).ConfigureAwait(false);
            if (restore.Status != "completed" || !restore.SessionCleared
                || restore.PlayerFormId != 0x14 || restore.CameraTargetFormId != 0x14)
            {
                throw new InvalidDataException($"restore_failed:{restore.FailureReason}");
            }

            string[] scores = runtime.Decision?.Scores
                .Select(item => $"{item.Id}={item.Score:R}")
                .ToArray() ?? [];
            return new DominatusSkyrimReport(
                MarionetteWireProtocol.Version,
                hello.SessionId!,
                observation.ActorId.FormId,
                observation.ActorId.Generation,
                target,
                observation.Position,
                final.Position,
                observation.Position.DistanceTo(target),
                final.Position.DistanceTo(target),
                runtime.SelectedOption ?? "unavailable",
                scores,
                "MoveToward",
                commandRequestId,
                movement.ActionLifecycle ?? [],
                observation.Sequence,
                final.Sequence,
                movement.RuntimeSequence,
                stopwatch.ElapsedMilliseconds,
                "bounded_direct_displacement",
                Distance(movement.PlayerPositionBefore, movement.PlayerPositionAfter),
                movement.CameraTargetFormId,
                foregroundAtDecision,
                foregroundAtActuation,
                transition.ToString(),
                true,
                restore.SessionCleared,
                restore.PlayerFormId,
                restore.CameraTargetFormId);
        }
        finally
        {
            if (restore is null)
            {
                HostMutationResult emergency = await SendMutationAsync(
                    pipe,
                    new EmergencyRestoreRequest(
                        MarionetteWireProtocol.Version,
                        "emergency_restore",
                        Guid.NewGuid().ToString("N"),
                        2000),
                    CancellationToken.None).ConfigureAwait(false);
                if (emergency.Status != "completed" || !emergency.SessionCleared)
                {
                    throw new InvalidDataException($"emergency_restore_failed:{emergency.FailureReason}");
                }
            }
        }
    }

    public static HostActorObservation ToActorObservation(SkyrimStateResult state)
    {
        if (!state.ActorObservationAvailable || !state.ActorGeneration.HasValue
            || !state.ActorFormId.HasValue || state.RuntimeSequence == 0)
        {
            throw new InvalidDataException("actor_observation_unavailable");
        }

        HostVelocity3? velocity = state.ActorVelocityX.HasValue
            && state.ActorVelocityY.HasValue && state.ActorVelocityZ.HasValue
            ? new HostVelocity3(
                state.ActorVelocityX.Value,
                state.ActorVelocityY.Value,
                state.ActorVelocityZ.Value)
            : null;
        return new HostActorObservation(
            new HostActorId(state.ActorFormId.Value, state.ActorGeneration.Value),
            new HostPosition3(state.ActorPositionX, state.ActorPositionY, state.ActorPositionZ),
            state.ActorHeadingRadians,
            velocity,
            state.ActorDead ? HostActorLifeState.Dead : HostActorLifeState.Alive,
            state.ActorMoving ? HostActorMovementState.Moving : HostActorMovementState.Idle,
            state.ActorLoaded,
            state.ActorCellFormId,
            CurrentTarget: null,
            DistanceToGoal: null,
            HostActionState.None,
            new HostCapabilitySnapshot(
                ParseCapability(state.BoundedDirectDisplacementCapability),
                ParseCapability(state.AnimatedLocomotionCapability),
                ParseCapability(state.GoalDirectedMovementCapability),
                ParseCapability(state.CameraFollowingCapability),
                ParseCapability(state.ActorActivationCapability),
                ParseCapability(state.AttackCapability),
                ParseCapability(state.JumpCapability),
                ParseCapability(state.SneakCapability)),
            state.RuntimeSequence);
    }

    private static HostCapabilitySupport ParseCapability(string value) => value switch
    {
        "supported" => HostCapabilitySupport.Supported,
        "experimental" => HostCapabilitySupport.Experimental,
        _ => HostCapabilitySupport.Unsupported,
    };
}

internal sealed class ConnectedMarionetteHostBackend : IHostPresenterBackend
{
    private readonly Stream pipe;
    private readonly Queue<HostRuntimeObservation> observations = new();
    private ulong nextEnvelopeSequence;

    public ConnectedMarionetteHostBackend(Stream pipe, HostActorObservation initialActor)
    {
        this.pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        CurrentActor = initialActor ?? throw new ArgumentNullException(nameof(initialActor));
        nextEnvelopeSequence = initialActor.Sequence;
    }

    public HostActorObservation CurrentActor { get; private set; }

    public HostMutationResult? MovementResult { get; private set; }

    public async ValueTask<HostCommandReceipt> SubmitAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        HostCommandValidationResult validation = request.Validate();
        if (!validation.IsValid || request.Arguments is not MoveTowardArguments move)
        {
            return new HostCommandReceipt(
                request.RequestId,
                Accepted: false,
                CurrentActor.Sequence,
                validation.FailureReason ?? "unsupported_command");
        }

        var wireRequest = new MoveTowardRequest(
            MarionetteWireProtocol.Version,
            "move_toward",
            request.RequestId.ToString("N"),
            checked((uint)request.ExpectedHostGeneration),
            move.ActorId.FormId,
            move.ExpectedObservationSequence,
            move.TargetPosition.X,
            move.TargetPosition.Y,
            move.TargetPosition.Z,
            move.StoppingDistance,
            move.MaximumDistance,
            move.SpeedPolicy == HostMovementSpeedPolicy.Walk ? "walk" : "run",
            checked((int)request.Timeout.TotalMilliseconds));
        await MarionetteWireProtocol.WriteAsync(pipe, wireRequest, cancellationToken).ConfigureAwait(false);
        MovementResult = await MarionetteWireProtocol.ReadAsync<HostMutationResult>(
            pipe,
            cancellationToken).ConfigureAwait(false);
        if (MovementResult.MessageKind != "move_toward_result"
            || MovementResult.RequestId != wireRequest.RequestId)
        {
            throw new InvalidDataException("move_toward_correlation_invalid");
        }

        HostActorObservation initial = CurrentActor;
        SkyrimStateResult finalState = await QueryStateAsync(cancellationToken).ConfigureAwait(false);
        HostActorObservation final = finalState.ActorObservationAvailable
            ? MarionetteTransportClient.ToActorObservation(finalState)
            : CurrentActor with
            {
                Loaded = false,
                MovementState = HostActorMovementState.Unknown,
                Sequence = finalState.RuntimeSequence,
            };
        string[] lifecycle = MovementResult.ActionLifecycle ?? ["rejected"];
        foreach (string state in lifecycle)
        {
            HostActionState mapped = MapState(state);
            Enqueue(
                request.RequestId,
                mapped,
                mapped is HostActionState.Accepted or HostActionState.Running ? initial : final,
                MovementResult.FailureReason);
        }
        CurrentActor = final;
        bool accepted = lifecycle.Contains("accepted", StringComparer.Ordinal);
        return new HostCommandReceipt(
            request.RequestId,
            accepted,
            MovementResult.RuntimeSequence,
            accepted ? null : MovementResult.FailureReason);
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

    private async ValueTask<SkyrimStateResult> QueryStateAsync(CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(
            pipe,
            new SkyrimStateRequest(
                MarionetteWireProtocol.Version,
                "query_skyrim_state",
                requestId,
                2000),
            cancellationToken).ConfigureAwait(false);
        SkyrimStateResult result = await MarionetteWireProtocol.ReadAsync<SkyrimStateResult>(
            pipe,
            cancellationToken).ConfigureAwait(false);
        if (result.Status != "completed" || result.RequestId != requestId)
        {
            throw new InvalidDataException("post_action_observation_failed");
        }
        return result;
    }

    private void Enqueue(
        Guid requestId,
        HostActionState state,
        HostActorObservation final,
        string? failureReason)
    {
        HostActorObservation actor = final with
        {
            MovementState = state == HostActionState.Running
                ? HostActorMovementState.Moving
                : HostActorMovementState.Idle,
            ActionState = state,
        };
        string? reason = state is HostActionState.Accepted or HostActionState.Running
            or HostActionState.Completed ? null : failureReason;
        var action = new HostActionResult(requestId, state, reason, actor);
        observations.Enqueue(new HostRuntimeObservation(
            ++nextEnvelopeSequence,
            ActiveHost: null,
            new PlayerAnchorObservation(0x14, 0, 0, 0),
            new CameraObservation(actor.ActorId.FormId, HostCameraMode.ThirdPerson),
            new CrosshairObservation(0),
            new MovementObservation(false, state == HostActionState.Running),
            action,
            actor));
    }

    private static HostActionState MapState(string state) => state switch
    {
        "accepted" => HostActionState.Accepted,
        "in_progress" => HostActionState.Running,
        "completed" => HostActionState.Completed,
        "blocked" => HostActionState.Blocked,
        "interrupted" => HostActionState.Interrupted,
        "target_invalid" => HostActionState.TargetInvalid,
        "actor_unloaded" => HostActionState.ActorUnloaded,
        "unsupported" => HostActionState.Unsupported,
        "timed_out" => HostActionState.TimedOut,
        "engine_refused" => HostActionState.EngineRefused,
        "rejected" => HostActionState.Rejected,
        _ => HostActionState.Failed,
    };
}
