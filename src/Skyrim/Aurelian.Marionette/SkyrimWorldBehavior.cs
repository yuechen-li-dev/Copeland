using System.Text.Json;

namespace Marionette.Skyrim.App;

public sealed record WorldNpcRequest(
    int ProtocolVersion, string MessageKind, string RequestId, int TimeoutMilliseconds,
    uint Radius = 4096, uint MaxResults = 8, uint TargetFormId = 0,
    ulong ExpectedWorldGeneration = 0, ulong ExpectedObservationSequence = 0,
    float ExpectedBaseSpeed = 0, float DesiredBaseSpeed = 0);

public sealed record WorldNpcObservation(
    uint FormId, uint BaseFormId, uint CellFormId, string Name,
    float PositionX, float PositionY, float PositionZ, float Distance,
    float BaseSpeedMultiplier, float SpeedMultiplier, bool Loaded, bool Dead);

public sealed record WorldNpcResult(
    int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence,
    string Status, string FailureReason, ulong WorldGeneration, ulong ObservationSequence,
    float BeforeBaseSpeed, float AfterBaseSpeed, WorldNpcObservation[] Npcs);

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
        float desired, CancellationToken cancellationToken)
    {
        if (source.WorldGeneration != State.WorldGeneration || !source.Npcs.Contains(npc))
        {
            throw new SkyrimWorldCommandDeferredException("stale_npc_observation");
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
            DesiredBaseSpeed: desired), cancellationToken);
    }

    private async ValueTask<WorldNpcResult> SendAsync(WorldNpcRequest request, CancellationToken cancellationToken)
    {
        if (!State.WorldReady || State.WorldPaused)
        {
            throw new SkyrimWorldCommandDeferredException("world_not_command_ready");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        await MarionetteWireProtocol.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
        WorldNpcResult result = await MarionetteWireProtocol.ReadAsync<WorldNpcResult>(pipe, timeout.Token).ConfigureAwait(false);
        if (result.ProtocolVersion != 1 || result.MessageKind != "world_npc_result"
            || result.RequestId != request.RequestId || result.ServerSequence == 0)
        {
            throw new InvalidDataException("world_npc_response_correlation_invalid");
        }
        if (result.Status != "completed")
        {
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
