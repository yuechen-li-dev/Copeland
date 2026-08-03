using Aurelian.Actuation.Host;

namespace Aurelian.Marionette.Transport;

public sealed record SkyrimLifecycleProcessingResult(
    bool Accepted,
    string Outcome,
    SkyrimCheckpointResult? Checkpoint = null);

/// <summary>
/// Lowers bounded Marionette lifecycle observations into portable world facts.
/// Checkpoint work stays out of the native callback and uses Dominatus.Core only.
/// </summary>
public sealed class SkyrimLiveLifecycleCoordinator
{
    private readonly SkyrimSessionId session;
    private readonly SkyrimCheckpointStore checkpoints;
    private readonly BodyBindingRegistry bindings;
    private readonly string restoreSessionScope;
    private readonly Dictionary<long, SkyrimSaveIdentity> saves = new();
    private readonly Dictionary<long, SkyrimSaveIdentity> loads = new();
    private long lastLifecycleSequence;
    private long nextWorldFactSequence;

    public SkyrimLiveLifecycleCoordinator(
        SkyrimSessionId session,
        SkyrimWorldOwnerRuntime world,
        SkyrimCheckpointStore checkpoints,
        BodyBindingRegistry bindings,
        string restoreSessionScope)
    {
        this.session = session;
        CurrentWorld = world ?? throw new ArgumentNullException(nameof(world));
        this.checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
        this.bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreSessionScope);
        this.restoreSessionScope = restoreSessionScope;
        nextWorldFactSequence = world.LastFactSequence;
    }

    public SkyrimWorldOwnerRuntime CurrentWorld { get; private set; }

    public void AbortPendingSaves()
    {
        foreach (long operationId in saves.Keys.ToArray())
        {
            checkpoints.Abort(operationId);
        }
        saves.Clear();
    }

    public SkyrimLifecycleProcessingResult Process(LifecycleObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        long sequence = checked((long)observation.Sequence);
        long operationId = checked((long)observation.OperationId);
        if (sequence <= lastLifecycleSequence)
        {
            return new SkyrimLifecycleProcessingResult(false, "stale_or_duplicate_lifecycle_sequence");
        }
        lastLifecycleSequence = sequence;
        nextWorldFactSequence = Math.Max(nextWorldFactSequence, CurrentWorld.LastFactSequence);
        long worldSequence = ++nextWorldFactSequence;

        return observation.Kind switch
        {
            "save_started" => StartSave(observation, worldSequence, operationId),
            "save_serialized" => CompleteSave(observation, worldSequence, operationId),
            "load_started" => StartLoad(observation, worldSequence, operationId),
            "load_completed" => CompleteLoad(observation, worldSequence, operationId),
            "load_failed" => FailLoad(observation, worldSequence, operationId),
            "new_game_started" => StartNewGame(observation, worldSequence, operationId),
            "revert_occurred" => ObserveRevert(observation, worldSequence, operationId),
            _ => throw new InvalidDataException($"unknown_lifecycle_kind:{observation.Kind}"),
        };
    }

    private SkyrimLifecycleProcessingResult StartSave(
        LifecycleObservation observation,
        long sequence,
        long operationId)
    {
        AbortPendingSaves();
        SkyrimSaveIdentity save = CreateIdentity(observation, operationId);
        CurrentWorld.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.SaveOperationStarted,
            sequence,
            Timeline: save.Timeline,
            Save: save,
            OperationId: operationId,
            SourceCallback: observation.SourceCallback));
        Tick(CurrentWorld);
        SkyrimCheckpointResult staged = checkpoints.CaptureProvisional(
            CurrentWorld,
            save,
            bindings,
            operationId);
        if (staged.Status != SkyrimCheckpointStatus.Staged)
        {
            return new SkyrimLifecycleProcessingResult(false, "save_checkpoint_not_staged", staged);
        }
        saves[operationId] = save;
        return new SkyrimLifecycleProcessingResult(true, "save_checkpoint_staged", staged);
    }

    private SkyrimLifecycleProcessingResult CompleteSave(
        LifecycleObservation observation,
        long sequence,
        long operationId)
    {
        if (!saves.Remove(operationId, out SkyrimSaveIdentity? save))
        {
            return new SkyrimLifecycleProcessingResult(false, "save_operation_not_staged");
        }
        SkyrimCheckpointResult committed = checkpoints.Commit(operationId);
        if (!committed.Completed)
        {
            return new SkyrimLifecycleProcessingResult(false, "save_checkpoint_not_committed", committed);
        }
        CurrentWorld.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.SaveOperationCompleted,
            sequence,
            Timeline: save.Timeline,
            Save: save,
            OperationId: operationId,
            SourceCallback: observation.SourceCallback));
        Tick(CurrentWorld);
        return new SkyrimLifecycleProcessingResult(true, "save_checkpoint_committed", committed);
    }

    private SkyrimLifecycleProcessingResult StartLoad(
        LifecycleObservation observation,
        long sequence,
        long operationId)
    {
        SkyrimSaveIdentity save = CreateIdentity(observation, operationId);
        loads[operationId] = save;
        CurrentWorld.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.LoadOperationStarted,
            sequence,
            Timeline: save.Timeline,
            Save: save,
            OperationId: operationId,
            SourceCallback: observation.SourceCallback));
        Tick(CurrentWorld);
        if (bindings.HasActiveExclusiveBinding)
        {
            CurrentWorld.RequireRestoration("active_binding_release_unconfirmed");
            return new SkyrimLifecycleProcessingResult(false, "active_binding_release_unconfirmed");
        }
        return new SkyrimLifecycleProcessingResult(true, "load_started");
    }

    private SkyrimLifecycleProcessingResult CompleteLoad(
        LifecycleObservation observation,
        long sequence,
        long operationId)
    {
        if (!loads.Remove(operationId, out SkyrimSaveIdentity? started))
        {
            return new SkyrimLifecycleProcessingResult(false, "load_operation_not_started");
        }
        if (CurrentWorld.RestorationIsRequired)
        {
            return new SkyrimLifecycleProcessingResult(false, "restoration_required_before_load_completion");
        }
        SkyrimSaveIdentity loaded = CreateIdentity(observation with { SaveName = started.SaveName }, operationId);
        bool rollback = CurrentWorld.Timeline is not null
            && loaded.Timeline.GameTime.GameDays < CurrentWorld.Timeline.GameTime.GameDays;
        CurrentWorld.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.LoadOperationCompleted,
            sequence,
            Timeline: loaded.Timeline,
            Save: loaded,
            OperationId: operationId,
            SourceCallback: observation.SourceCallback));
        Tick(CurrentWorld);
        SkyrimCheckpointResult restored = checkpoints.Restore(loaded, restoreSessionScope);
        if (restored.Status == SkyrimCheckpointStatus.CheckpointUnavailable)
        {
            // The controller may attach after Skyrim has already loaded a world. That
            // baseline load has no managed history to restore and remains command-ready.
            CurrentWorld.CompleteUntrackedLoad();
            return new SkyrimLifecycleProcessingResult(true, "load_completed_without_checkpoint", restored);
        }
        if (!restored.Completed || restored.RestoredWorld is null || restored.Entry is null)
        {
            CurrentWorld.RequireRestoration(restored.FailureReason ?? restored.Status.ToString());
            return new SkyrimLifecycleProcessingResult(false, "checkpoint_restore_failed", restored);
        }
        CurrentWorld = restored.RestoredWorld;
        CurrentWorld.CompleteCheckpointRestore(
            loaded,
            restored.Entry.CheckpointArtifactId,
            rollback);
        CurrentWorld.Tick();
        return new SkyrimLifecycleProcessingResult(true, "checkpoint_restored", restored);
    }

    private SkyrimLifecycleProcessingResult FailLoad(
        LifecycleObservation observation,
        long sequence,
        long operationId)
    {
        if (!loads.Remove(operationId, out SkyrimSaveIdentity? save))
        {
            return new SkyrimLifecycleProcessingResult(false, "load_operation_not_started");
        }
        CurrentWorld.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.LoadOperationFailed,
            sequence,
            Timeline: save.Timeline,
            Save: save,
            OperationId: operationId,
            SourceCallback: observation.SourceCallback));
        Tick(CurrentWorld);
        return new SkyrimLifecycleProcessingResult(true, "load_failed_without_restore");
    }

    private SkyrimLifecycleProcessingResult StartNewGame(
        LifecycleObservation observation,
        long sequence,
        long operationId)
    {
        SkyrimTimelineStamp? timeline = CreateOptionalTimeline(observation, operationId);
        CurrentWorld.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.NewGameStarted,
            sequence,
            Timeline: timeline,
            OperationId: operationId,
            SourceCallback: observation.SourceCallback));
        Tick(CurrentWorld);
        loads.Clear();
        return new SkyrimLifecycleProcessingResult(true, "new_game_started");
    }

    private SkyrimLifecycleProcessingResult ObserveRevert(
        LifecycleObservation observation,
        long sequence,
        long operationId)
    {
        CurrentWorld.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.RevertOccurred,
            sequence,
            Timeline: CreateOptionalTimeline(observation, operationId),
            OperationId: operationId,
            SourceCallback: observation.SourceCallback));
        Tick(CurrentWorld);
        return new SkyrimLifecycleProcessingResult(true, "revert_observed");
    }

    private SkyrimSaveIdentity CreateIdentity(LifecycleObservation observation, long operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.SaveName);
        SkyrimTimelineStamp timeline = CreateOptionalTimeline(observation, operationId)
            ?? throw new InvalidDataException("lifecycle_game_timestamp_missing");
        return new SkyrimSaveIdentity(observation.SaveName, timeline, operationId).Validate();
    }

    private SkyrimTimelineStamp? CreateOptionalTimeline(LifecycleObservation observation, long operationId) =>
        observation.GameTimeDays.HasValue
            ? new SkyrimTimelineStamp(
                session,
                new SkyrimGameTimestamp(observation.GameTimeDays.Value),
                operationId)
            : null;

    private static void Tick(SkyrimWorldOwnerRuntime owner)
    {
        for (int index = 0; index < 6; index++)
        {
            owner.Tick();
        }
    }
}
