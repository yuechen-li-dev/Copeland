using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Aurelian.Actuation.Host;
using Marionette.Skyrim;

namespace Marionette.Skyrim.App;

public sealed record DominatusSkyrimReport(
    int ProtocolVersion,
    string SessionId,
    string AgentId,
    string BodyId,
    string[] CandidateAgentIds,
    string[] CandidateBodyIds,
    string BindingState,
    uint ActorFormId,
    ulong ActorGeneration,
    HostPosition3 Target,
    HostPosition3 InitialPosition,
    HostPosition3 FinalPosition,
    float DistanceBefore,
    float DistanceAfter,
    string SelectedUtilityOption,
    string[] UtilityScores,
    string[] SelectedUtilityFactors,
    string MovementUtilityOption,
    bool WrongAgentMovementRejected,
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
        if (!initial.GameTimeDays.HasValue)
        {
            throw new InvalidDataException("skyrim_game_timestamp_unavailable");
        }

        var importedAgents = new ImportedAgentRegistry(hello.SessionId!);
        var worldOwner = new SkyrimWorldOwnerRuntime(hello.SessionId!, importedAgents);
        SkyrimSessionId semanticSession = CreateSessionId(hello.SessionId!);
        var initialTimeline = new SkyrimTimelineStamp(
            semanticSession,
            new SkyrimGameTimestamp(initial.GameTimeDays.Value),
            checked((long)initial.RuntimeSequence));
        worldOwner.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
        TickWorldOwner(worldOwner);
        worldOwner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.WorldReady,
            2,
            Timeline: initialTimeline));
        TickWorldOwner(worldOwner);
        if (!worldOwner.CanIssueBodyCommands)
        {
            throw new InvalidDataException("skyrim_world_owner_not_ready");
        }

        StableHostCandidateQuery query = await QueryStableHostCandidatesAsync(
            pipe,
            cancellationToken).ConfigureAwait(false);
        SkyrimCandidateSet candidateSet = SkyrimCandidateLowerer.Lower(
            hello.SessionId!,
            query.First,
            importedAgents);
        if (candidateSet.Candidates.Count < 2)
        {
            throw new InvalidDataException("multiple_candidate_bodies_required");
        }
        long bodyFactSequence = 3;
        foreach (AgentBodyCandidate candidate in candidateSet.Candidates)
        {
            SkyrimBodyCandidateMapping mapping = candidateSet.BackendMappings[candidate.Body.Id];
            worldOwner.Post(new SkyrimWorldFact(
                SkyrimWorldFactKind.BodyLoaded,
                bodyFactSequence++,
                Body: candidate.Body,
                Origin: mapping.Origin,
                ImportedData: candidate.Agent.Data));
            TickWorldOwner(worldOwner);
        }

        var selection = new SkyrimCandidateSelectionRuntime();
        if (!selection.PublishCandidates(candidateSet.Candidates)
            || selection.RunUntilTerminal() != SkyrimCandidateSelectionState.Completed
            || selection.SelectedCandidate is null)
        {
            throw new InvalidDataException("candidate_agent_selection_failed");
        }
        AgentBodyCandidate selected = selection.SelectedCandidate;
        SkyrimBodyCandidateMapping selectedMapping = candidateSet.BackendMappings[selected.Body.Id];
        EvaluateHostRequestResult evaluate = await SendEvaluateAsync(
            pipe,
            new EvaluateHostRequestRequest(
                MarionetteWireProtocol.Version,
                "evaluate_host_request",
                Guid.NewGuid().ToString("N"),
                selectedMapping.ActorFormId,
                2000),
            cancellationToken).ConfigureAwait(false);
        if (evaluate.Status != "completed" || !evaluate.Eligible
            || !evaluate.PendingRequestGeneration.HasValue)
        {
            throw new InvalidDataException($"host_request_failed:{evaluate.FailureReason ?? evaluate.EligibilityReason}");
        }

        SkyrimStateResult pending = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!pending.PendingRequestGeneration.HasValue || !pending.PendingTargetFormId.HasValue)
        {
            throw new InvalidDataException("pending_body_materialization_missing");
        }
        if (pending.PendingTargetFormId.Value != selectedMapping.ActorFormId)
        {
            throw new InvalidDataException("selected_body_materialization_mismatch");
        }

        selected = SkyrimCandidateLowerer.RefreshSelectedGeneration(
            selected,
            pending.PendingRequestGeneration.Value,
            importedAgents);

        HostMutationResult? restore = null;
        try
        {
            AgentId agentId = selected.Agent.Id;
            BodyId bodyId = selected.Body.Id;
            var connected = new ConnectedMarionetteHostBackend(
                pipe,
                new HostActorId(
                    pending.PendingTargetFormId.Value,
                    pending.PendingRequestGeneration.Value));
            var backend = new BodyBindingHostBackend(connected);
            backend.RegisterCandidate(
                bodyId,
                new HostActorId(
                    pending.PendingTargetFormId.Value,
                    pending.PendingRequestGeneration.Value));
            var definition = SkyrimAgent.Define(
                agentId,
                bodyId,
                pending.PendingRequestGeneration.Value,
                new ReachTargetGoal(
                    new HostPosition3(0.0f, 64.0f, 0.0f),
                    StoppingDistance: 16.0f,
                    RelativeToBoundPosition: true),
                new ApproachTargetOption(
                    MaximumDistance: 64.0f,
                    HostMovementSpeedPolicy.Walk));
            SkyrimBodyAgentRuntime runtime = definition.CreateRuntime(backend);

            bool foregroundAtDecision = IsSkyrimForeground();
            var stopwatch = Stopwatch.StartNew();
            for (int index = 0; index < 32 && runtime.State != SkyrimBodyAgentState.BoundIdle; index++)
            {
                runtime.Tick();
            }
            if (runtime.State != SkyrimBodyAgentState.BoundIdle
                || runtime.Body is null
                || runtime.Binding is null)
            {
                throw new InvalidDataException("selected_agent_binding_not_observed");
            }

            AgentBodyCandidate nonSelected = candidateSet.Candidates.First(
                candidate => candidate.Agent.Id != selected.Agent.Id);
            HostActionResult wrongOwner = await HostActionRunner.ExecuteAsync(
                backend,
                new HostCommandRequest(
                    Guid.NewGuid(),
                    runtime.Binding.Generation,
                    HostCommandKind.MoveBodyToward,
                    TimeSpan.FromSeconds(2),
                    new MoveBodyTowardArguments(
                        nonSelected.Agent.Id,
                        bodyId,
                        runtime.Body.Position,
                        0.0f,
                        1.0f,
                        HostMovementSpeedPolicy.Walk,
                        runtime.Binding.Generation,
                        runtime.Body.Sequence)),
                cancellationToken).ConfigureAwait(false);
            bool wrongAgentRejected = wrongOwner.State == HostActionState.Rejected;
            if (!wrongAgentRejected)
            {
                throw new InvalidDataException("non_selected_agent_command_not_rejected");
            }

            SkyrimBodyAgentState transition = runtime.RunUntilTerminal();
            stopwatch.Stop();
            bool foregroundAtActuation = IsSkyrimForeground();
            HostActorObservation observation = connected.InitialActor
                ?? throw new InvalidDataException("bound_body_observation_missing");
            HostActorObservation final = connected.CurrentActor
                ?? throw new InvalidDataException("final_body_observation_missing");
            HostPosition3 target = runtime.ResolvedTarget
                ?? throw new InvalidDataException("movement_target_missing");
            HostMutationResult begin = connected.BeginResult
                ?? throw new InvalidDataException("binding_result_missing");
            HostMutationResult movement = connected.MovementResult
                ?? throw new InvalidDataException("movement_result_missing");
            restore = connected.RestoreResult;
            if (transition != SkyrimBodyAgentState.Completed
                || movement.ActionState != "completed"
                || runtime.Binding?.State != BodyBindingState.Released
                || final.Position.DistanceTo(target) > 16.5f)
            {
                throw new InvalidDataException(
                    $"dominatus_transition_failed:{transition}:{movement.ActionState}");
            }
            if (restore is null || restore.Status != "completed" || !restore.SessionCleared
                || restore.PlayerFormId != 0x14 || restore.CameraTargetFormId != 0x14)
            {
                throw new InvalidDataException($"restore_failed:{restore?.FailureReason}");
            }

            if (!string.IsNullOrWhiteSpace(_config.CheckpointDirectory))
            {
                var checkpointStore = new SkyrimCheckpointStore(_config.CheckpointDirectory);
                SkyrimCheckpointResult checkpoint = checkpointStore.Capture(
                    worldOwner,
                    new SkyrimSaveIdentity("ed-m2b2d", initialTimeline),
                    new BodyBindingRegistry());
                if (!checkpoint.Completed)
                {
                    throw new InvalidDataException(
                        $"dominatus_checkpoint_failed:{checkpoint.FailureReason}");
                }
            }

            string[] scores = selection.Decision?.Scores
                .Select(item => $"{item.Id}={item.Score:R}")
                .ToArray() ?? [];
            CandidateUtilityReport selectedUtility = selection.UtilityReports.Single(
                report => report.Agent == selected.Agent.Id);
            return new DominatusSkyrimReport(
                MarionetteWireProtocol.Version,
                hello.SessionId!,
                agentId.ToString(),
                bodyId.Value,
                candidateSet.Candidates.Select(candidate => candidate.Agent.Id.ToString()).ToArray(),
                candidateSet.Candidates.Select(candidate => candidate.Body.Id.Value).ToArray(),
                runtime.Binding.State.ToString(),
                observation.ActorId.FormId,
                observation.ActorId.Generation,
                target,
                observation.Position,
                final.Position,
                observation.Position.DistanceTo(target),
                final.Position.DistanceTo(target),
                selection.Decision?.BestId ?? "unavailable",
                scores,
                selectedUtility.Factors
                    .Select(factor => $"{factor.Name}={factor.Value:R}*{factor.Weight:R}:{factor.Contribution:R}")
                    .ToArray(),
                runtime.SelectedOption ?? "unavailable",
                wrongAgentRejected,
                "MoveBodyToward",
                runtime.MovementResult!.RequestId,
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
            if (restore is null || restore.Status != "completed" || !restore.SessionCleared)
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

    private static SkyrimSessionId CreateSessionId(string transportSessionId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"aurelian-skyrim-session\n{transportSessionId}"));
        byte[] guidBytes = hash[..16];
        guidBytes[7] = (byte)((guidBytes[7] & 0x0f) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return new SkyrimSessionId(new Guid(guidBytes));
    }

    private static void TickWorldOwner(SkyrimWorldOwnerRuntime owner)
    {
        for (int index = 0; index < 6; index++)
        {
            owner.Tick();
        }
    }
}

internal sealed class ConnectedMarionetteHostBackend : IHostPresenterBackend
{
    private readonly Stream pipe;
    private readonly HostActorId candidateIdentity;
    private readonly Queue<HostRuntimeObservation> observations = new();
    private ulong nextEnvelopeSequence;

    public ConnectedMarionetteHostBackend(Stream pipe, HostActorId candidateIdentity)
    {
        this.pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        if (!candidateIdentity.IsValid)
        {
            throw new ArgumentException("Candidate identity must be valid.", nameof(candidateIdentity));
        }

        this.candidateIdentity = candidateIdentity;
    }

    public HostActorObservation? InitialActor { get; private set; }

    public HostActorObservation? CurrentActor { get; private set; }

    public HostMutationResult? BeginResult { get; private set; }

    public HostMutationResult? MovementResult { get; private set; }

    public HostMutationResult? RestoreResult { get; private set; }

    public async ValueTask<HostCommandReceipt> SubmitAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        HostCommandValidationResult validation = request.Validate();
        if (!validation.IsValid)
        {
            return new HostCommandReceipt(
                request.RequestId,
                Accepted: false,
                nextEnvelopeSequence,
                validation.FailureReason);
        }

        return request.Kind switch
        {
            HostCommandKind.BeginHostSession => await BeginAsync(request, cancellationToken).ConfigureAwait(false),
            HostCommandKind.MoveToward => await MoveAsync(
                request,
                (MoveTowardArguments)request.Arguments,
                cancellationToken).ConfigureAwait(false),
            HostCommandKind.EndHostSession => await RestoreAsync(request, cancellationToken).ConfigureAwait(false),
            _ => new HostCommandReceipt(
                request.RequestId,
                Accepted: false,
                nextEnvelopeSequence,
                "unsupported_command"),
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

    private async ValueTask<HostCommandReceipt> BeginAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        var wireRequest = new BeginHostSessionRequest(
            MarionetteWireProtocol.Version,
            "begin_host_session",
            request.RequestId.ToString("N"),
            checked((uint)candidateIdentity.Generation),
            candidateIdentity.FormId,
            checked((int)request.Timeout.TotalMilliseconds));
        await MarionetteWireProtocol.WriteAsync(pipe, wireRequest, cancellationToken).ConfigureAwait(false);
        BeginResult = await MarionetteWireProtocol.ReadAsync<HostMutationResult>(
            pipe,
            cancellationToken).ConfigureAwait(false);
        ValidateCorrelation(BeginResult, "begin_host_session_result", wireRequest.RequestId);

        HostActionState state = MapState(BeginResult.ActionState ?? BeginResult.Status);
        HostActorObservation? actor = null;
        if (state == HostActionState.Completed)
        {
            SkyrimStateResult active = await QueryStateAsync(cancellationToken).ConfigureAwait(false);
            actor = MarionetteTransportClient.ToActorObservation(active);
            InitialActor = actor;
            CurrentActor = actor;
        }

        Enqueue(request.RequestId, state, actor, BeginResult.FailureReason);
        return new HostCommandReceipt(request.RequestId, true, BeginResult.RuntimeSequence, null);
    }

    private async ValueTask<HostCommandReceipt> MoveAsync(
        HostCommandRequest request,
        MoveTowardArguments move,
        CancellationToken cancellationToken)
    {
        if (CurrentActor is null)
        {
            return new HostCommandReceipt(
                request.RequestId,
                Accepted: false,
                nextEnvelopeSequence,
                "host_session_not_active");
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
            : initial with
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

    private async ValueTask<HostCommandReceipt> RestoreAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        var wireRequest = new RestoreHostSessionRequest(
            MarionetteWireProtocol.Version,
            "restore_host_session",
            request.RequestId.ToString("N"),
            checked((uint)request.ExpectedHostGeneration),
            checked((int)request.Timeout.TotalMilliseconds));
        await MarionetteWireProtocol.WriteAsync(pipe, wireRequest, cancellationToken).ConfigureAwait(false);
        RestoreResult = await MarionetteWireProtocol.ReadAsync<HostMutationResult>(
            pipe,
            cancellationToken).ConfigureAwait(false);
        ValidateCorrelation(RestoreResult, "restore_host_session_result", wireRequest.RequestId);
        HostActionState state = MapState(RestoreResult.ActionState ?? RestoreResult.Status);
        Enqueue(request.RequestId, state, CurrentActor, RestoreResult.FailureReason);
        return new HostCommandReceipt(request.RequestId, true, RestoreResult.RuntimeSequence, null);
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
        HostActorObservation? final,
        string? failureReason)
    {
        HostActorObservation? actor = final is null ? null : final with
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
            new CameraObservation(actor?.ActorId.FormId ?? 0, HostCameraMode.ThirdPerson),
            new CrosshairObservation(0),
            new MovementObservation(false, state == HostActionState.Running),
            action,
            actor));
    }

    private static void ValidateCorrelation(
        HostMutationResult result,
        string messageKind,
        string requestId)
    {
        if (result.MessageKind != messageKind || result.RequestId != requestId)
        {
            throw new InvalidDataException($"{messageKind}_correlation_invalid");
        }
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
