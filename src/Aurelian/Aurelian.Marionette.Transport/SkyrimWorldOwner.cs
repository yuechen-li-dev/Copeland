using Aurelian.Actuation.Host;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using AurelianAgentId = Aurelian.Actuation.Host.AgentId;

namespace Aurelian.Marionette.Transport;

public enum SkyrimWorldOwnerState
{
    Disconnected,
    AwaitingWorld,
    WorldReady,
    WorldPaused,
    SaveLoading,
    RestoringCheckpoint,
    RollbackDetected,
    RestorationRequired,
    ShuttingDown,
    Failed,
}

/// <summary>
/// Dominatus-backed semantic owner for one Skyrim connection/session. Native
/// callbacks enter as ordered facts through the owner's mailbox; portable
/// lifecycle events leave through the existing per-agent event bus.
/// </summary>
public sealed class SkyrimWorldOwnerRuntime
{
    private readonly AiWorld world = new();
    private readonly AiAgent owner;
    private readonly Dictionary<AurelianAgentId, AiAgent> importedRuntimeAgents = new();
    private long lastFactSequence;
    private SkyrimWorldOwnerState state = SkyrimWorldOwnerState.Disconnected;
    private SkyrimWorldOwnerState routedState = SkyrimWorldOwnerState.Disconnected;
    private long activeLoadOperationId;

    public SkyrimWorldOwnerRuntime(string sessionScope, ImportedAgentRegistry? registry = null)
    {
        Registry = registry ?? new ImportedAgentRegistry(sessionScope);
        owner = new AiAgent(SkyrimWorldOwnerFlow.Define(this).CreateBrain());
        world.Add(owner);
        world.Tick(0.0f);
    }

    public ImportedAgentRegistry Registry { get; }

    public SkyrimWorldOwnerState State => state;

    public SkyrimTimelineStamp? Timeline { get; private set; }

    public SkyrimSaveIdentity? CurrentSave { get; private set; }

    public bool CanIssueBodyCommands => state == SkyrimWorldOwnerState.WorldReady;

    public bool RestorationIsRequired => state == SkyrimWorldOwnerState.RestorationRequired;

    public FlowInspection FlowInspection => SkyrimWorldOwnerFlow.Define(this).Inspect();

    internal AiWorld DominatusWorld => world;

    internal AiAgent DominatusOwner => owner;

    internal long LastFactSequence => lastFactSequence;

    public bool Post(SkyrimWorldFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return world.Mail.Send(owner.Id, fact);
    }

    public void Tick() => world.Tick(0.01f);

    public void RunUntilSettled(int maximumTicks = 32)
    {
        for (int index = 0; index < maximumTicks; index++)
        {
            SkyrimWorldOwnerState before = state;
            world.Tick(0.01f);
            if (before == state)
            {
                return;
            }
        }

        throw new TimeoutException("Skyrim world owner did not settle.");
    }

    public bool TryConsume<T>(ref EventCursor cursor, out T value) where T : notnull =>
        owner.Events.TryConsume(ref cursor, filter: null, out value);

    internal void Enter(SkyrimWorldOwnerState next)
    {
        state = next;
        owner.Bb.Set(SkyrimWorldCheckpointKeys.OwnerState, next.ToString());
    }

    internal StateId RouteState()
    {
        return routedState switch
        {
            SkyrimWorldOwnerState.Disconnected => SkyrimWorldOwnerFlow.States.Disconnected,
            SkyrimWorldOwnerState.AwaitingWorld => SkyrimWorldOwnerFlow.States.AwaitingWorld,
            SkyrimWorldOwnerState.WorldReady => SkyrimWorldOwnerFlow.States.WorldReady,
            SkyrimWorldOwnerState.WorldPaused => SkyrimWorldOwnerFlow.States.WorldPaused,
            SkyrimWorldOwnerState.SaveLoading => SkyrimWorldOwnerFlow.States.SaveLoading,
            SkyrimWorldOwnerState.RestoringCheckpoint => SkyrimWorldOwnerFlow.States.RestoringCheckpoint,
            SkyrimWorldOwnerState.RollbackDetected => SkyrimWorldOwnerFlow.States.RollbackDetected,
            SkyrimWorldOwnerState.RestorationRequired => SkyrimWorldOwnerFlow.States.RestorationRequired,
            SkyrimWorldOwnerState.ShuttingDown => SkyrimWorldOwnerFlow.States.ShuttingDown,
            _ => SkyrimWorldOwnerFlow.States.Failed,
        };
    }

    internal void Process(SkyrimWorldFact fact)
    {
        if (fact.Sequence <= lastFactSequence)
        {
            routedState = state;
            return;
        }

        lastFactSequence = fact.Sequence;
        routedState = fact.Kind switch
        {
            SkyrimWorldFactKind.BackendConnected => Connect(fact),
            SkyrimWorldFactKind.BackendDisconnected => Disconnect(fact),
            SkyrimWorldFactKind.WorldReady => Ready(fact),
            SkyrimWorldFactKind.WorldPaused => Pause(fact),
            SkyrimWorldFactKind.SaveLoading => BeginSaveLoad(fact),
            SkyrimWorldFactKind.SaveLoaded => FinishSaveLoad(fact),
            SkyrimWorldFactKind.SaveOperationStarted => StartSave(fact),
            SkyrimWorldFactKind.SaveOperationCompleted => CompleteSave(fact),
            SkyrimWorldFactKind.LoadOperationStarted => BeginLoad(fact),
            SkyrimWorldFactKind.LoadOperationCompleted => CompleteLoad(fact),
            SkyrimWorldFactKind.LoadOperationFailed => FailLoad(fact),
            SkyrimWorldFactKind.NewGameStarted => BeginNewGame(fact),
            SkyrimWorldFactKind.RevertOccurred => ObserveRevert(fact),
            SkyrimWorldFactKind.BodyLoaded => LoadBody(fact),
            SkyrimWorldFactKind.BodyLost => LoseBody(fact),
            SkyrimWorldFactKind.RestorationRequired => RequireRestoration(fact),
            SkyrimWorldFactKind.ShutdownRequested => Shutdown(fact),
            _ => SkyrimWorldOwnerState.Failed,
        };
    }

    private SkyrimWorldOwnerState Connect(SkyrimWorldFact fact)
    {
        owner.Events.Publish(new SkyrimBackendConnected(fact.Sequence));
        return SkyrimWorldOwnerState.AwaitingWorld;
    }

    private SkyrimWorldOwnerState Disconnect(SkyrimWorldFact fact)
    {
        owner.Events.Publish(new SkyrimBackendDisconnected(fact.Sequence, fact.Reason));
        return SkyrimWorldOwnerState.Disconnected;
    }

    private SkyrimWorldOwnerState Ready(SkyrimWorldFact fact)
    {
        SkyrimTimelineStamp timeline = fact.Timeline
            ?? throw new InvalidDataException("world_ready_timeline_missing");
        if (Timeline is not null)
        {
            owner.Events.Publish(new SkyrimTimelineChanged(Timeline, timeline));
        }
        Timeline = timeline;
        WriteTimeline(timeline);
        owner.Events.Publish(new SkyrimWorldReady(timeline));
        return SkyrimWorldOwnerState.WorldReady;
    }

    private SkyrimWorldOwnerState Pause(SkyrimWorldFact fact)
    {
        SkyrimTimelineStamp timeline = fact.Timeline ?? Timeline
            ?? throw new InvalidDataException("world_pause_timeline_missing");
        owner.Events.Publish(new SkyrimWorldPaused(timeline));
        return SkyrimWorldOwnerState.WorldPaused;
    }

    private SkyrimWorldOwnerState BeginSaveLoad(SkyrimWorldFact fact)
    {
        SkyrimSaveIdentity save = fact.Save?.Validate()
            ?? throw new InvalidDataException("save_loading_identity_missing");
        owner.Events.Publish(new SkyrimSaveLoading(save));
        return SkyrimWorldOwnerState.SaveLoading;
    }

    private SkyrimWorldOwnerState StartSave(SkyrimWorldFact fact)
    {
        SkyrimSaveIdentity save = fact.Save?.Validate()
            ?? throw new InvalidDataException("save_started_identity_missing");
        owner.Events.Publish(new SkyrimSaveOperationStarted(
            fact.OperationId,
            save,
            fact.SourceCallback ?? "unknown"));
        return state;
    }

    private SkyrimWorldOwnerState CompleteSave(SkyrimWorldFact fact)
    {
        SkyrimSaveIdentity save = fact.Save?.Validate()
            ?? throw new InvalidDataException("save_completed_identity_missing");
        owner.Events.Publish(new SkyrimSaveOperationCompleted(
            fact.OperationId,
            save,
            fact.SourceCallback ?? "unknown"));
        CurrentSave = save;
        Timeline = save.Timeline;
        WriteTimeline(save.Timeline);
        return state;
    }

    private SkyrimWorldOwnerState BeginLoad(SkyrimWorldFact fact)
    {
        SkyrimSaveIdentity save = fact.Save?.Validate()
            ?? throw new InvalidDataException("load_started_identity_missing");
        if (fact.OperationId <= 0)
        {
            throw new InvalidDataException("load_operation_id_invalid");
        }
        activeLoadOperationId = fact.OperationId;
        owner.Events.Publish(new SkyrimLoadOperationStarted(
            fact.OperationId,
            save,
            fact.SourceCallback ?? "unknown"));
        owner.Events.Publish(new ReleaseAllBindings("skyrim_load_started"));
        return SkyrimWorldOwnerState.SaveLoading;
    }

    private SkyrimWorldOwnerState CompleteLoad(SkyrimWorldFact fact)
    {
        if (fact.OperationId != activeLoadOperationId)
        {
            return state;
        }
        SkyrimSaveIdentity save = fact.Save?.Validate()
            ?? throw new InvalidDataException("load_completed_identity_missing");
        owner.Events.Publish(new SkyrimLoadOperationCompleted(
            fact.OperationId,
            save,
            fact.SourceCallback ?? "unknown"));
        activeLoadOperationId = 0;
        return FinishSaveLoad(fact);
    }

    private SkyrimWorldOwnerState FailLoad(SkyrimWorldFact fact)
    {
        if (fact.OperationId != activeLoadOperationId)
        {
            return state;
        }
        SkyrimSaveIdentity save = fact.Save?.Validate()
            ?? throw new InvalidDataException("load_failed_identity_missing");
        owner.Events.Publish(new SkyrimLoadOperationFailed(
            fact.OperationId,
            save,
            fact.SourceCallback ?? "unknown"));
        activeLoadOperationId = 0;
        return SkyrimWorldOwnerState.WorldReady;
    }

    private SkyrimWorldOwnerState BeginNewGame(SkyrimWorldFact fact)
    {
        CurrentSave = null;
        Timeline = fact.Timeline;
        activeLoadOperationId = 0;
        owner.Events.Publish(new SkyrimNewGameStarted(fact.OperationId, fact.Timeline));
        return SkyrimWorldOwnerState.AwaitingWorld;
    }

    private SkyrimWorldOwnerState ObserveRevert(SkyrimWorldFact fact)
    {
        owner.Events.Publish(new SkyrimRevertOccurred(fact.OperationId, fact.Timeline));
        return state;
    }

    internal void CompleteCheckpointRestore(
        SkyrimSaveIdentity save,
        string checkpointArtifactId,
        bool rollback)
    {
        Timeline = save.Timeline;
        CurrentSave = save;
        state = SkyrimWorldOwnerState.WorldReady;
        routedState = SkyrimWorldOwnerState.WorldReady;
        owner.Events.Publish(new SkyrimWorldRestored(save, checkpointArtifactId));
        if (rollback)
        {
            owner.Events.Publish(new SkyrimTimelineRebased(save.Timeline, checkpointArtifactId));
        }
    }

    internal void CompleteUntrackedLoad()
    {
        state = SkyrimWorldOwnerState.WorldReady;
        routedState = SkyrimWorldOwnerState.WorldReady;
    }

    internal void RequireRestoration(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        state = SkyrimWorldOwnerState.RestorationRequired;
        routedState = SkyrimWorldOwnerState.RestorationRequired;
        owner.Events.Publish(new SkyrimRestorationRequired(reason));
    }

    private SkyrimWorldOwnerState FinishSaveLoad(SkyrimWorldFact fact)
    {
        SkyrimSaveIdentity loaded = fact.Save?.Validate()
            ?? throw new InvalidDataException("save_loaded_identity_missing");
        bool rollback = Timeline is not null
            && loaded.Timeline.GameTime.GameDays < Timeline.GameTime.GameDays;
        if (rollback)
        {
            owner.Events.Publish(new SkyrimRollbackDetected(Timeline!, loaded.Timeline));
        }

        Timeline = loaded.Timeline;
        CurrentSave = loaded;
        WriteTimeline(loaded.Timeline);
        owner.Bb.Set(SkyrimWorldCheckpointKeys.SaveName, loaded.SaveName);
        owner.Events.Publish(new SkyrimSaveLoaded(loaded));
        return rollback
            ? SkyrimWorldOwnerState.RollbackDetected
            : SkyrimWorldOwnerState.RestoringCheckpoint;
    }

    private SkyrimWorldOwnerState LoadBody(SkyrimWorldFact fact)
    {
        BodyObservation body = fact.Body
            ?? throw new InvalidDataException("body_loaded_observation_missing");
        SkyrimActorOrigin origin = fact.Origin
            ?? throw new InvalidDataException("body_loaded_origin_missing");
        ImportedNpcData data = fact.ImportedData
            ?? throw new InvalidDataException("body_loaded_import_data_missing");
        ImportedAgentResolution resolution = Registry.ResolveOrCreate(body, data, origin);
        if (!resolution.Accepted)
        {
            throw new InvalidDataException(resolution.FailureReason);
        }

        EnsureImportedRuntimeAgent(resolution.Agent!);

        owner.Events.Publish(new SkyrimBodyLoaded(
            resolution.Agent!.Id,
            body,
            origin,
            Rematerialized: !resolution.Created));
        return state;
    }

    private SkyrimWorldOwnerState LoseBody(SkyrimWorldFact fact)
    {
        BodyObservation body = fact.Body
            ?? throw new InvalidDataException("body_lost_observation_missing");
        ImportedNpcAgent? agent = Registry.Find(body.Id);
        if (agent is not null && Registry.MarkBodyLost(body.Id))
        {
            owner.Events.Publish(new SkyrimBodyLost(agent.Id, body.Id));
        }
        return state;
    }

    private SkyrimWorldOwnerState RequireRestoration(SkyrimWorldFact fact)
    {
        owner.Events.Publish(new SkyrimRestorationRequired(
            fact.Reason ?? "backend_outcome_uncertain"));
        return SkyrimWorldOwnerState.RestorationRequired;
    }

    private SkyrimWorldOwnerState Shutdown(SkyrimWorldFact fact)
    {
        owner.Events.Publish(new SkyrimSessionShuttingDown(fact.Sequence));
        return SkyrimWorldOwnerState.ShuttingDown;
    }

    internal void RestoreSemanticStateFromBlackboards()
    {
        string stateName = owner.Bb.GetOrDefault(
            SkyrimWorldCheckpointKeys.OwnerState,
            SkyrimWorldOwnerState.AwaitingWorld.ToString());
        state = Enum.TryParse(stateName, out SkyrimWorldOwnerState restoredState)
            ? restoredState
            : SkyrimWorldOwnerState.Failed;
        Guid session = owner.Bb.GetOrDefault(SkyrimWorldCheckpointKeys.SessionId, Guid.Empty);
        double gameDays = owner.Bb.GetOrDefault(SkyrimWorldCheckpointKeys.GameDays, -1.0);
        long sequence = owner.Bb.GetOrDefault(SkyrimWorldCheckpointKeys.TimelineSequence, -1L);
        if (session != Guid.Empty && gameDays >= 0 && sequence >= 0)
        {
            Timeline = new SkyrimTimelineStamp(
                new SkyrimSessionId(session),
                new SkyrimGameTimestamp(gameDays),
                sequence);
        }

        foreach (AiAgent runtimeAgent in importedRuntimeAgents.Values)
        {
            ImportedNpcAgent imported = SkyrimWorldCheckpointKeys.ReadImportedAgent(runtimeAgent);
            Registry.RegisterRestored(imported);
        }
    }

    internal void AddRestoredImportedRuntimeAgent(ImportedNpcAgent imported)
    {
        EnsureImportedRuntimeAgent(imported);
    }

    private void WriteTimeline(SkyrimTimelineStamp timeline)
    {
        owner.Bb.Set(SkyrimWorldCheckpointKeys.SessionId, timeline.Session.Value);
        owner.Bb.Set(SkyrimWorldCheckpointKeys.GameDays, timeline.GameTime.GameDays);
        owner.Bb.Set(SkyrimWorldCheckpointKeys.TimelineSequence, timeline.Sequence);
    }

    private void EnsureImportedRuntimeAgent(ImportedNpcAgent imported)
    {
        if (imported.Provenance.SkyrimOrigin?.Kind != SkyrimActorOriginKind.PlacedPluginReference
            || importedRuntimeAgents.ContainsKey(imported.Id))
        {
            return;
        }

        var runtimeAgent = new AiAgent(SkyrimCandidateMailboxFlow.Define().CreateBrain());
        SkyrimWorldCheckpointKeys.WriteImportedAgent(runtimeAgent, imported);
        world.Add(runtimeAgent);
        importedRuntimeAgents.Add(imported.Id, runtimeAgent);
    }
}

internal static class SkyrimWorldCheckpointKeys
{
    internal static readonly BbKey<string> OwnerState = new("skyrim.world.state");
    internal static readonly BbKey<Guid> SessionId = new("skyrim.timeline.session");
    internal static readonly BbKey<double> GameDays = new("skyrim.timeline.game-days");
    internal static readonly BbKey<long> TimelineSequence = new("skyrim.timeline.sequence");
    internal static readonly BbKey<string> SaveName = new("skyrim.save.name");
    internal static readonly BbKey<Guid> SemanticAgentId = new("skyrim.import.agent-id");
    internal static readonly BbKey<string> PluginName = new("skyrim.import.plugin");
    internal static readonly BbKey<long> LocalFormId = new("skyrim.import.local-form-id");
    internal static readonly BbKey<string> DisplayName = new("skyrim.import.display-name");
    internal static readonly BbKey<string> Archetype = new("skyrim.import.archetype");
    internal static readonly BbKey<bool> Humanoid = new("skyrim.import.humanoid");
    internal static readonly BbKey<bool> Essential = new("skyrim.import.essential");
    internal static readonly BbKey<bool> Protected = new("skyrim.import.protected");

    internal static void WriteImportedAgent(AiAgent runtimeAgent, ImportedNpcAgent imported)
    {
        SkyrimPlacedActorOrigin placed = imported.Provenance.SkyrimOrigin?.Placed
            ?? throw new InvalidDataException("placed_agent_origin_missing");
        runtimeAgent.Bb.Set(SemanticAgentId, imported.Id.Value);
        runtimeAgent.Bb.Set(PluginName, placed.PluginName);
        runtimeAgent.Bb.Set(LocalFormId, (long)placed.LocalFormId);
        runtimeAgent.Bb.Set(DisplayName, imported.Data.Identity.DisplayName);
        runtimeAgent.Bb.Set(Archetype, imported.Data.Identity.Archetype);
        runtimeAgent.Bb.Set(Humanoid, imported.Data.Body.Humanoid);
        runtimeAgent.Bb.Set(Essential, imported.Data.Body.Essential);
        runtimeAgent.Bb.Set(Protected, imported.Data.Body.Protected);
    }

    internal static ImportedNpcAgent ReadImportedAgent(AiAgent runtimeAgent)
    {
        var placed = new SkyrimPlacedActorOrigin(
            runtimeAgent.Bb.GetOrDefault(PluginName, string.Empty),
            checked((uint)runtimeAgent.Bb.GetOrDefault(LocalFormId, 0L)));
        var origin = SkyrimActorOrigin.ForPlaced(placed);
        return new ImportedNpcAgent(
            new AurelianAgentId(runtimeAgent.Bb.GetOrDefault(SemanticAgentId, Guid.Empty)),
            new AgentProvenance(
                AgentProvenanceKind.ImportedLegacy,
                "Skyrim/Marionette",
                origin.StableKey)
            {
                SkyrimOrigin = origin,
            },
            new ImportedNpcData(
                new IdentityProfile(
                    runtimeAgent.Bb.GetOrDefault(DisplayName, string.Empty),
                    runtimeAgent.Bb.GetOrDefault(Archetype, string.Empty)),
                new BodyProfile(
                    runtimeAgent.Bb.GetOrDefault(Humanoid, false),
                    runtimeAgent.Bb.GetOrDefault(Essential, false),
                    runtimeAgent.Bb.GetOrDefault(Protected, false)),
                SelectionProfile.ImportedDefault));
    }
}

public static partial class SkyrimWorldOwnerFlow
{
    [DominatusFlow("aurelian.skyrim.world-owner.m3")]
    public static partial FlowDefinition Define(SkyrimWorldOwnerRuntime runtime);

    [DominatusState("aurelian.skyrim.world.disconnected", Root = true)]
    private static IEnumerator<AiStep> Disconnected(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.Disconnected);

    [DominatusState("aurelian.skyrim.world.awaiting-world")]
    private static IEnumerator<AiStep> AwaitingWorld(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.AwaitingWorld);

    [DominatusState("aurelian.skyrim.world.ready")]
    private static IEnumerator<AiStep> WorldReady(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.WorldReady);

    [DominatusState("aurelian.skyrim.world.paused")]
    private static IEnumerator<AiStep> WorldPaused(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.WorldPaused);

    [DominatusState("aurelian.skyrim.world.save-loading")]
    private static IEnumerator<AiStep> SaveLoading(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.SaveLoading);

    [DominatusState("aurelian.skyrim.world.restoring-checkpoint")]
    private static IEnumerator<AiStep> RestoringCheckpoint(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.RestoringCheckpoint);

    [DominatusState("aurelian.skyrim.world.rollback-detected")]
    private static IEnumerator<AiStep> RollbackDetected(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.RollbackDetected);

    [DominatusState("aurelian.skyrim.world.restoration-required")]
    private static IEnumerator<AiStep> RestorationRequired(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.RestorationRequired);

    [DominatusState("aurelian.skyrim.world.shutting-down")]
    private static IEnumerator<AiStep> ShuttingDown(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.ShuttingDown);

    [DominatusState("aurelian.skyrim.world.failed")]
    private static IEnumerator<AiStep> Failed(AiCtx context, SkyrimWorldOwnerRuntime runtime) =>
        AwaitFact(runtime, SkyrimWorldOwnerState.Failed);

    private static IEnumerator<AiStep> AwaitFact(
        SkyrimWorldOwnerRuntime runtime,
        SkyrimWorldOwnerState state)
    {
        runtime.Enter(state);
        yield return Ai.Event<SkyrimWorldFact>(
            onConsumed: (_, fact) => runtime.Process(fact),
            cursorStart: EventCursorStart.FutureOnly);
        yield return Ai.Goto(runtime.RouteState(), "ordered Skyrim world fact handled");
    }
}
