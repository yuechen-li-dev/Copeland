using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace Marionette.Skyrim.App;

/// <summary>Serial transport/lifecycle adapter for the ordinary-world Dominatus NPC.</summary>
public sealed class OwnedNpcAgentBehavior(TimeSpan? startupDelay = null) : ISkyrimWorldBehavior
{
    private OwnedNpcAgentRuntime? runtime;
    private OwnedNpcIdentity? identity;
    private ulong generation;
    private float readySeconds;
    private long lastTimestamp;
    private long nextReport;
    private string lastReportedState = "";
    private int loadTicks;
    private uint cellFormId;
    private OwnedNpcAgentSaveState? restorePlan;
    private OwnedNpcAgentSaveState? restoredCheckpoint;
    private OwnedNpcAgentSaveState? frozenSave;
    private bool preparingSave;
    public bool SavePrepared { get; private set; }

    public OwnedNpcAgentStatus? Status => runtime?.Status;

    public async ValueTask PrepareSaveAsync(SkyrimHostContext context, CancellationToken cancellationToken)
    {
        SkyrimWorldOwnerRuntime owner = context.WorldOwner
            ?? throw new InvalidOperationException("owned_agent_checkpoint_owner_missing");
        if (!preparingSave)
        {
            frozenSave = runtime?.Capture(cellFormId) ?? owner.ReadOwnedAgentState();
            preparingSave = true;
            SavePrepared = false;
        }
        if (frozenSave is not null) owner.WriteOwnedAgentState(frozenSave);
        WorldNpcResult source = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
        if (source.OwnedActiveCount != 0 || source.OwnedMovement?.MarkerActive == true)
        {
            if (identity is null || source.OwnedNpc != identity)
                throw new InvalidDataException("owned_agent_save_foreign_body");
            await context.ReleaseOwnedNpcAsync(identity, cancellationToken).ConfigureAwait(false);
            throw new SkyrimWorldCommandDeferredException("owned_agent_save_retirement_pending");
        }
        SavePrepared = true;
        LogCheckpoint("SKYRIM_AGENT_SAVE_PREPARED", frozenSave);
    }

    public void CancelSavePreparation()
    {
        if (!preparingSave) return;
        ResumeAfterSave();
    }

    public void ObserveLifecycle(LifecycleObservation observation, SkyrimLifecycleProcessingResult result,
        SkyrimWorldOwnerRuntime owner)
    {
        if (observation.Kind == "save_serialized" && result.Accepted && preparingSave)
        {
            LogCheckpoint("SKYRIM_AGENT_SAVE_COMMITTED", frozenSave);
            ResumeAfterSave();
        }
        else if (observation.Kind == "load_started" || observation.Kind == "new_game_started")
        {
            preparingSave = false;
            SavePrepared = false;
            frozenSave = null;
        }
        else if (result.Outcome == "checkpoint_restored")
        {
            restoredCheckpoint = owner.ReadOwnedAgentState();
            LogCheckpoint("SKYRIM_AGENT_CHECKPOINT_LOADED", restoredCheckpoint);
        }
    }

    private void ResumeAfterSave()
    {
        restorePlan = frozenSave;
        frozenSave = null;
        preparingSave = false;
        SavePrepared = false;
        runtime = null;
        identity = null;
        loadTicks = 0;
        lastTimestamp = 0;
        lastReportedState = "";
    }

    private static void LogCheckpoint(string label, OwnedNpcAgentSaveState? saved)
    {
        Console.WriteLine(label + " " + JsonSerializer.Serialize(saved,
            OwnedNpcCheckpointJsonContext.Default.OwnedNpcAgentSaveState));
    }

    public async ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken)
    {
        long now = Stopwatch.GetTimestamp();
        float delta = 0;
        if (lastTimestamp != 0)
        {
            double elapsed = Stopwatch.GetElapsedTime(lastTimestamp, now).TotalSeconds;
            if (elapsed <= 1)
            {
                delta = (float)elapsed;
            }
        }
        lastTimestamp = now;
        if (generation != context.State.WorldGeneration)
        {
            // Only committed semantic state selected by the lifecycle owner can
            // cross worlds. Native identities and in-flight work never do.
            generation = context.State.WorldGeneration;
            runtime = null;
            identity = null;
            restorePlan = restoredCheckpoint;
            restoredCheckpoint = null;
            readySeconds = 0;
            loadTicks = 0;
            lastReportedState = "";
            Console.WriteLine($"SKYRIM_AGENT_WORLD_RESET generation={generation}");
            if (restorePlan is null) context.WorldOwner?.ClearOwnedAgentState();
        }
        if (preparingSave) return;
        if (restorePlan is not null && context.State.PlayerCellFormId != 0
            && !restorePlan.CanMaterialize(context.State.PlayerCellFormId,
                new(context.State.PlayerPositionX, context.State.PlayerPositionY, context.State.PlayerPositionZ)))
        {
            if (lastReportedState != "dormant")
                Console.WriteLine("SKYRIM_AGENT_DORMANT reason=outside_saved_route_region");
            lastReportedState = "dormant";
            return;
        }
        readySeconds += delta;
        if (readySeconds < (startupDelay ?? TimeSpan.FromSeconds(8)).TotalSeconds)
        {
            return;
        }

        WorldNpcResult source = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
        if (identity is null)
        {
            if (restorePlan?.Mode == "stopped")
            {
                return;
            }
            if (source.OwnedNpcState == "retiring")
            {
                return;
            }
            if (source.OwnedActiveCount != 0 || source.OwnedMovement?.MarkerActive == true)
            {
                throw new InvalidDataException("owned_agent_body_already_owned");
            }
            WorldNpcResult spawned = await context.SpawnOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
            identity = spawned.OwnedNpc ?? throw new InvalidDataException("owned_agent_spawn_identity_missing");
            return;
        }
        if (source.OwnedNpc != identity)
        {
            throw new InvalidDataException("owned_agent_body_identity_changed");
        }
        WorldNpcObservation? actor = source.Npcs.SingleOrDefault();
        bool available = source.OwnedNpcState == "live" && actor is { Loaded: true, Dead: false };
        if (runtime is null)
        {
            if (!available)
            {
                if (++loadTicks > 100)
                {
                    throw new InvalidDataException("owned_agent_body_never_loaded");
                }
                return;
            }
            cellFormId = actor!.CellFormId;
            if (restorePlan is not null && restorePlan.CellFormId != cellFormId)
                throw new InvalidDataException("owned_agent_checkpoint_cell_mismatch");
            runtime = new OwnedNpcAgentRuntime(Fact(source, actor, true), saved: restorePlan);
            restorePlan = null;
            Console.WriteLine("SKYRIM_AGENT_BOUND " + JsonSerializer.Serialize(identity,
                MarionetteWireJsonContext.Default.OwnedNpcIdentity));
        }
        else
        {
            Vector3 position = actor is null ? Vector3.Zero : Position(actor);
            runtime.Observe(new OwnedNpcAgentFact(identity, source.ObservationSequence,
                available, position, source.OwnedMovement));
        }
        runtime.Tick(delta);
        OwnedNpcAgentIntent? intent = runtime.TakeIntent();
        if (intent is not null)
        {
            await DispatchAsync(context, source, intent, cancellationToken).ConfigureAwait(false);
        }
        context.WorldOwner?.WriteOwnedAgentState(runtime.Capture(cellFormId));
        OwnedNpcAgentStatus status = runtime.Status;
        if (status.State != lastReportedState || now >= nextReport)
        {
            Console.WriteLine("SKYRIM_AGENT " + JsonSerializer.Serialize(status,
                MarionetteWireJsonContext.Default.OwnedNpcAgentStatus));
            lastReportedState = status.State;
            nextReport = now + Stopwatch.Frequency;
        }
    }

    private async ValueTask DispatchAsync(SkyrimHostContext context, WorldNpcResult source,
        OwnedNpcAgentIntent intent, CancellationToken cancellationToken)
    {
        if (intent.Identity != identity || source.ObservationSequence != intent.ObservationSequence)
        {
            throw new InvalidDataException("owned_agent_intent_source_changed");
        }
        try
        {
            WorldNpcResult result;
            ulong movementId = 0;
            if (intent.Action == OwnedNpcAgentAction.Walk)
            {
                result = await context.MoveOwnedNpcAsync(source, intent.Identity,
                    intent.Target.X, intent.Target.Y, intent.Target.Z, cancellationToken,
                    durationMilliseconds: 12000).ConfigureAwait(false);
                OwnedNpcMovement movement = result.OwnedMovement
                    ?? throw new InvalidDataException("owned_agent_movement_receipt_missing");
                if (result.OwnedNpc != identity || movement.State is not ("walking" or "stopping" or "arrived")
                    || movement.MovementId == 0)
                {
                    throw new InvalidDataException("owned_agent_movement_receipt_invalid");
                }
                movementId = movement.MovementId;
            }
            else if (source.OwnedActiveCount == 0 && source.OwnedMovement?.MarkerActive != true)
            {
                // An explicit shell release or native lifecycle cleanup may have
                // already retired this lease. Do not respawn it in the same world.
                result = source;
            }
            else
            {
                result = await context.ReleaseOwnedNpcAsync(intent.Identity, cancellationToken).ConfigureAwait(false);
            }
            runtime!.Receive(new OwnedNpcAgentReceipt(intent.Id, intent.Identity, true, movementId, result.FailureReason));
        }
        catch (SkyrimWorldCommandDeferredException exception)
        {
            // Only an explicit refusal is retryable. An I/O timeout or uncertain
            // write tears down the controller and uses native disconnect cleanup.
            runtime!.Receive(new OwnedNpcAgentReceipt(intent.Id, intent.Identity, false, 0, exception.Message));
        }
    }

    private OwnedNpcAgentFact Fact(WorldNpcResult source, WorldNpcObservation actor, bool available)
    {
        return new OwnedNpcAgentFact(identity!, source.ObservationSequence, available,
            Position(actor), source.OwnedMovement);
    }

    private static Vector3 Position(WorldNpcObservation actor)
    {
        return new Vector3(actor.PositionX, actor.PositionY, actor.PositionZ);
    }
}
