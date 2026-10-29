using System.Text.Json;

namespace Marionette.Skyrim.App;

public sealed record WorldNpcRequest(
    int ProtocolVersion, string MessageKind, string RequestId, int TimeoutMilliseconds,
    uint Radius = 4096, uint MaxResults = 8, uint TargetFormId = 0,
    ulong ExpectedWorldGeneration = 0, ulong ExpectedObservationSequence = 0,
    float ExpectedBaseSpeed = 0, float DesiredBaseSpeed = 0,
    ulong ExpectedOwnedLeaseId = 0, string NpcBaseKey = "",
    float TargetPositionX = 0, float TargetPositionY = 0, float TargetPositionZ = 0,
    float StoppingDistance = 48, uint MovementDurationMilliseconds = 10000, ulong ExpectedMovementId = 0);

public sealed record OwnedNpcIdentity(ulong WorldGeneration, ulong LeaseId, uint FormId, uint BaseFormId);

public sealed record WorldNpcObservation(
    uint FormId, uint BaseFormId, uint CellFormId, string Name,
    float PositionX, float PositionY, float PositionZ, float Distance,
    float BaseSpeedMultiplier, float SpeedMultiplier, bool Loaded, bool Dead,
    bool Moving = false, bool AiEnabled = false, bool AnimationSpeedAvailable = false, float AnimationSpeed = 0);

public sealed record OwnedNpcMovement(ulong MovementId, string State, string Reason,
    float TargetX, float TargetY, float TargetZ, float StoppingDistance, float Distance,
    float ElapsedSeconds, uint MarkerFormId, bool MarkerActive, float StopRequestedSeconds = 0);

public sealed record WorldNpcResult(
    int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence,
    string Status, string FailureReason, ulong WorldGeneration, ulong ObservationSequence,
    float BeforeBaseSpeed, float AfterBaseSpeed, WorldNpcObservation[] Npcs,
    OwnedNpcIdentity? OwnedNpc = null, string OwnedNpcState = "none",
    ulong OwnedCreatedCount = 0, ulong OwnedRetiredCount = 0, uint OwnedActiveCount = 0,
    uint OwnedBaseLiveCount = 0, bool OwnedBaseCountComplete = false, OwnedNpcMovement? OwnedMovement = null);

public sealed record NpcNumberProof(
    ulong WorldGeneration, uint FormId, uint BaseFormId, string Name,
    float Before, float Changed, float ObservedChanged, float Restored);

/// <summary>Ordinary C# behavior, called serially only while the lifecycle owner permits commands.</summary>
public interface ISkyrimWorldBehavior
{
    ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken);
}

public sealed class SkyrimWorldCommandDeferredException(string reason) : Exception(reason);

public sealed class SkyrimHostContext
{
    private readonly Stream pipe;
    private WorldNpcResult? observation;

    internal SkyrimHostContext(Stream pipe, SkyrimStateResult state)
    {
        this.pipe = pipe;
        State = state;
    }

    public SkyrimStateResult State { get; }

    public ValueTask<WorldNpcResult> MoveOwnedNpcAsync(WorldNpcResult source, OwnedNpcIdentity identity,
        float x, float y, float z, CancellationToken cancellationToken, float stoppingDistance = 48,
        uint durationMilliseconds = 10000, string? expectedFailure = null)
    {
        if (source.OwnedNpc != identity || source.WorldGeneration != State.WorldGeneration
            || source.OwnedNpcState != "live")
        {
            throw new SkyrimWorldCommandDeferredException("owned_npc_identity_mismatch");
        }
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)
            || !float.IsFinite(stoppingDistance) || stoppingDistance is < 16 or > 128
            || durationMilliseconds is < 500 or > 15000)
        {
            throw new ArgumentOutOfRangeException(nameof(stoppingDistance));
        }
        return SendAsync(new WorldNpcRequest(1, "move_owned_npc", Guid.NewGuid().ToString("N"), 2000,
            TargetFormId: identity.FormId, ExpectedWorldGeneration: identity.WorldGeneration,
            ExpectedOwnedLeaseId: identity.LeaseId, ExpectedObservationSequence: source.ObservationSequence,
            TargetPositionX: x, TargetPositionY: y, TargetPositionZ: z, StoppingDistance: stoppingDistance,
            MovementDurationMilliseconds: durationMilliseconds), cancellationToken, expectedFailure);
    }

    public ValueTask<WorldNpcResult> CancelOwnedMovementAsync(OwnedNpcIdentity identity, ulong movementId,
        CancellationToken cancellationToken, string? expectedFailure = null)
    {
        return SendAsync(new WorldNpcRequest(1, "cancel_owned_npc_movement", Guid.NewGuid().ToString("N"), 2000,
            TargetFormId: identity.FormId, ExpectedWorldGeneration: identity.WorldGeneration,
            ExpectedOwnedLeaseId: identity.LeaseId, ExpectedMovementId: movementId), cancellationToken, expectedFailure);
    }

    public ValueTask<WorldNpcResult> ObserveOwnedNpcAsync(CancellationToken cancellationToken)
    {
        return SendAsync(new WorldNpcRequest(1, "query_owned_npc", Guid.NewGuid().ToString("N"), 2000), cancellationToken);
    }

    public ValueTask<WorldNpcResult> SpawnOwnedNpcAsync(CancellationToken cancellationToken, string? expectedFailure = null)
    {
        return SendAsync(new WorldNpcRequest(1, "spawn_owned_npc", Guid.NewGuid().ToString("N"), 2000,
            ExpectedWorldGeneration: State.WorldGeneration, NpcBaseKey: "hunter"), cancellationToken, expectedFailure);
    }

    public ValueTask<WorldNpcResult> ReleaseOwnedNpcAsync(OwnedNpcIdentity identity,
        CancellationToken cancellationToken, string? expectedFailure = null, uint? targetOverride = null)
    {
        return SendAsync(new WorldNpcRequest(1, "release_owned_npc", Guid.NewGuid().ToString("N"), 2000,
            TargetFormId: targetOverride ?? identity.FormId, ExpectedWorldGeneration: identity.WorldGeneration,
            ExpectedOwnedLeaseId: identity.LeaseId), cancellationToken, expectedFailure);
    }

    public async ValueTask<WorldNpcResult> ObserveNpcsAsync(CancellationToken cancellationToken, bool refresh = false)
    {
        if (observation is null || refresh)
        {
            observation = await SendAsync(new WorldNpcRequest(
                1, "query_world_npcs", Guid.NewGuid().ToString("N"), 2000), cancellationToken).ConfigureAwait(false);
        }
        return observation;
    }

    public ValueTask<WorldNpcResult> SetBaseSpeedAsync(WorldNpcResult source, WorldNpcObservation npc,
        float desired, CancellationToken cancellationToken, OwnedNpcIdentity? owned = null)
    {
        if (source.WorldGeneration != State.WorldGeneration || !source.Npcs.Contains(npc))
        {
            throw new SkyrimWorldCommandDeferredException("stale_npc_observation");
        }
        if (owned is not null && (owned.FormId != npc.FormId || owned.WorldGeneration != source.WorldGeneration
            || source.OwnedNpc != owned || source.OwnedNpcState != "live"))
        {
            throw new SkyrimWorldCommandDeferredException("owned_npc_identity_mismatch");
        }
        if (!float.IsFinite(desired) || Math.Abs(desired - npc.BaseSpeedMultiplier) > 10
            || desired is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(desired));
        }
        return SendAsync(new WorldNpcRequest(
            1, "set_npc_base_speed", Guid.NewGuid().ToString("N"), 2000,
            TargetFormId: npc.FormId,
            ExpectedWorldGeneration: source.WorldGeneration,
            ExpectedObservationSequence: source.ObservationSequence,
            ExpectedBaseSpeed: npc.BaseSpeedMultiplier,
            DesiredBaseSpeed: desired, ExpectedOwnedLeaseId: owned?.LeaseId ?? 0), cancellationToken);
    }

    private async ValueTask<WorldNpcResult> SendAsync(WorldNpcRequest request, CancellationToken cancellationToken,
        string? expectedFailure = null)
    {
        if ((!State.WorldReady || State.WorldPaused)
            && request.MessageKind is not ("query_owned_npc" or "release_owned_npc" or "cancel_owned_npc_movement"))
        {
            throw new SkyrimWorldCommandDeferredException("world_not_command_ready");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        await MarionetteWireProtocol.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
        WorldNpcResult result = await MarionetteWireProtocol.ReadAsync<WorldNpcResult>(pipe, timeout.Token).ConfigureAwait(false);
        if (request.MessageKind is "spawn_owned_npc" or "query_owned_npc" or "release_owned_npc"
            || request.ExpectedOwnedLeaseId != 0)
        {
            Console.WriteLine("SKYRIM_OWNED_COMMAND kind=" + request.MessageKind + " " +
                JsonSerializer.Serialize(result, MarionetteWireJsonContext.Default.WorldNpcResult));
        }
        if (result.ProtocolVersion != 1 || result.MessageKind != "world_npc_result"
            || result.RequestId != request.RequestId || result.ServerSequence == 0)
        {
            throw new InvalidDataException("world_npc_response_correlation_invalid");
        }
        if (result.Status != "completed")
        {
            if (expectedFailure is not null && result.FailureReason == expectedFailure)
            {
                return result;
            }
            if (request.MessageKind == "query_world_npcs" && result.FailureReason is
                "dispatch_timeout" or "dispatch_unavailable" or "queue_full")
            {
                throw new SkyrimWorldCommandDeferredException(result.FailureReason);
            }
            if (result.FailureReason is "world_not_command_ready" or "stale_npc_observation"
                or "npc_identity_or_load_changed")
            {
                throw new SkyrimWorldCommandDeferredException(result.FailureReason);
            }
            throw new InvalidDataException($"world_npc_command_failed:{result.FailureReason}");
        }
        if (expectedFailure is not null)
        {
            throw new InvalidDataException("expected_native_refusal_missing:" + expectedFailure);
        }
        if (result.WorldGeneration != State.WorldGeneration)
        {
            throw new SkyrimWorldCommandDeferredException("world_generation_changed");
        }
        return result;
    }
}

public static class SkyrimWorldBehaviorGate
{
    public static bool CanRun(SkyrimLiveLifecycleCoordinator coordinator, SkyrimStateResult state)
    {
        return coordinator.CanRunBehaviors && state.WorldReady && !state.WorldPaused
            && state.WorldGeneration != 0 && !state.ActiveHostSession;
    }

    public static async ValueTask TickAsync(SkyrimLiveLifecycleCoordinator coordinator, SkyrimStateResult state,
        SkyrimHostContext context, IReadOnlyList<ISkyrimWorldBehavior> behaviors, CancellationToken cancellationToken)
    {
        if (!CanRun(coordinator, state))
        {
            return;
        }
        foreach (ISkyrimWorldBehavior behavior in behaviors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await behavior.TickAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class ObserveWorldNpcsBehavior : ISkyrimWorldBehavior
{
    private DateTimeOffset nextObservation;

    public async ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < nextObservation)
        {
            return;
        }
        WorldNpcResult result = await context.ObserveNpcsAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine("SKYRIM_HOST_NPCS " + JsonSerializer.Serialize(result, MarionetteWireJsonContext.Default.WorldNpcResult));
        nextObservation = DateTimeOffset.UtcNow.AddSeconds(1);
    }
}

/// <summary>One reversible number proof per process, followed by ordinary observation ticks.</summary>
public sealed class NpcNumberProofBehavior : ISkyrimWorldBehavior
{
    public NpcNumberProof? CompletedProof { get; private set; }

    public async ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken)
    {
        if (CompletedProof is not null)
        {
            return;
        }
        WorldNpcResult source = await context.ObserveNpcsAsync(cancellationToken).ConfigureAwait(false);
        WorldNpcObservation? npc = source.Npcs.FirstOrDefault(candidate =>
            candidate.Loaded && !candidate.Dead && candidate.BaseSpeedMultiplier is >= 1 and <= 295);
        if (npc is null)
        {
            return;
        }
        float desired = npc.BaseSpeedMultiplier + 5;
        WorldNpcResult changed = await context.SetBaseSpeedAsync(source, npc, desired, cancellationToken).ConfigureAwait(false);
        try
        {
            WorldNpcResult observed = await context.ObserveNpcsAsync(cancellationToken, refresh: true).ConfigureAwait(false);
            WorldNpcObservation current = observed.Npcs.FirstOrDefault(candidate => candidate.FormId == npc.FormId)
                ?? throw new SkyrimWorldCommandDeferredException("npc_unloaded_during_number_proof");
            if (Math.Abs(current.BaseSpeedMultiplier - desired) > 0.001f)
            {
                throw new InvalidDataException("npc_number_change_not_observed");
            }
            WorldNpcResult restored = await context.SetBaseSpeedAsync(observed, current,
                npc.BaseSpeedMultiplier, cancellationToken).ConfigureAwait(false);
            if (Math.Abs(restored.AfterBaseSpeed - npc.BaseSpeedMultiplier) > 0.001f)
            {
                throw new InvalidDataException("npc_number_restore_not_observed");
            }
            CompletedProof = new NpcNumberProof(source.WorldGeneration, npc.FormId, npc.BaseFormId,
                npc.Name, npc.BaseSpeedMultiplier, changed.AfterBaseSpeed,
                current.BaseSpeedMultiplier, restored.AfterBaseSpeed);
            Console.WriteLine("SKYRIM_NPC_NUMBER_PROOF_PASSED " +
                JsonSerializer.Serialize(CompletedProof, MarionetteWireJsonContext.Default.NpcNumberProof));
        }
        finally
        {
            if (CompletedProof is null)
            {
                // The native journal also restores on disconnect, save, or load. A
                // successful same-world command restores immediately on behavior failure.
                try
                {
                    WorldNpcResult latest = await context.ObserveNpcsAsync(cancellationToken, refresh: true).ConfigureAwait(false);
                    WorldNpcObservation? current = latest.Npcs.FirstOrDefault(candidate => candidate.FormId == npc.FormId);
                    if (current is not null && Math.Abs(current.BaseSpeedMultiplier - desired) < 0.001f)
                    {
                        await context.SetBaseSpeedAsync(latest, current, npc.BaseSpeedMultiplier, cancellationToken).ConfigureAwait(false);
                    }
                    else if (current is null)
                    {
                        throw new InvalidDataException("npc_restore_requires_disconnect");
                    }
                }
                catch (SkyrimWorldCommandDeferredException)
                {
                    Console.Error.WriteLine("SKYRIM_HOST number_proof_interrupted_by_world_transition");
                }
            }
        }
    }
}
