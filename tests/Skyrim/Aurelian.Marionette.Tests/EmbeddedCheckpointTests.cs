using Marionette.Skyrim.App;
using Marionette.Skyrim;
using Aurelian.Actuation.Host;
using Dominatus.Core.Blackboard;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class EmbeddedCheckpointTests
{
    [Fact]
    public void CanonicalBytesCarryStateWithoutHistoricalIndexAcrossRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), "skyrim-embedded-" + Guid.NewGuid().ToString("N"));
        try
        {
            var owner = new SkyrimWorldOwnerRuntime("first-process");
            var state = new OwnedNpcAgentSaveState(1, Guid.NewGuid(), "owned-patrol-v1", 0x165A3,
                new(0, 0, 0), new(256, 256, 0), 1, 7, 0, "rest", 0.75f, 1.5f, 2);
            owner.WriteOwnedAgentState(state);
            var quest = new PatrolTrialState(1, state.AgentId, "return", 7, 2, state.CellFormId,
                state.Origin, 120);
            owner.WritePatrolTrial(quest);
            var store = new SkyrimCheckpointStore(path);
            string saved = store.ExportEmbedded(owner);
            Assert.StartsWith("444F4D31", saved);
            owner.WriteOwnedAgentState(state with { CompletedLegs = 99 });
            var restarted = new SkyrimCheckpointStore(path);
            var restored = restarted.RestoreEmbedded(saved, "second-process");
            Assert.Equal(state, restored.ReadOwnedAgentState());
            Assert.Equal(quest, restored.ReadPatrolTrial());
            Assert.Empty(restarted.ReadEntries());
            Assert.Single(Directory.GetFiles(path, "*.dom"));
            Assert.Empty(Directory.GetFiles(path, "*.tmp"));
            Assert.Throws<InvalidDataException>(() => restarted.RestoreEmbedded(saved + "00", "corrupt"));
            Assert.Throws<InvalidDataException>(() => restarted.RestoreEmbedded("broken", "corrupt"));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Fact]
    public void QuestSemanticStateRejectsInvalidProgressAndUnknownStages()
    {
        var state = new PatrolTrialState(1, Guid.NewGuid(), "accompany", 0, 0, 0x165A3, new(0, 0, 0));
        state.Validate();
        Assert.Throws<InvalidDataException>(() => (state with { Stage = "rewarded-twice" }).Validate());
        Assert.Throws<InvalidDataException>(() => (state with { AccompaniedLegs = 3 }).Validate());
        Assert.Throws<InvalidDataException>(() => (state with { GoldBeforeReward = -2 }).Validate());
    }

    [Fact]
    public void CorruptSelectedCosaveGatesWorldAndALaterGoodLoadCanRecover()
    {
        string path = Path.Combine(Path.GetTempPath(), "skyrim-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var original = new SkyrimWorldOwnerRuntime("saved-world");
            var session = new SkyrimSessionId(Guid.NewGuid());
            original.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
            for (int index = 0; index < 6; index++) original.Tick();
            original.Post(new SkyrimWorldFact(SkyrimWorldFactKind.WorldReady, 2,
                new SkyrimTimelineStamp(session, new SkyrimGameTimestamp(10), 1)));
            for (int index = 0; index < 6; index++) original.Tick();
            var store = new SkyrimCheckpointStore(path);
            string good = store.ExportEmbedded(original);
            var coordinator = new SkyrimLiveLifecycleCoordinator(session, original, store,
                new BodyBindingRegistry(), "restart");
            var save = new SkyrimSaveIdentity("Selected", new SkyrimTimelineStamp(session,
                new SkyrimGameTimestamp(10), 1), 1);
            coordinator.EmbeddedCheckpoint = new(1, "world_checkpoint_result", "bad", 1,
                "completed", 1, "corrupt", "");
            Assert.False(coordinator.RestoreEmbedded(save).Accepted);
            Assert.True(coordinator.CurrentWorld.RestorationIsRequired);
            Assert.Empty(store.ReadEntries());
            coordinator.EmbeddedCheckpoint = coordinator.EmbeddedCheckpoint with
                { LoadStatus = "ready", CheckpointHex = good };
            coordinator.Process(new LifecycleObservation("load_started", 1, 2, "Selected", 10, "native"));
            var recovered = coordinator.Process(new LifecycleObservation("load_completed", 2, 2, "Selected", 10, "native"));
            Assert.True(recovered.Accepted);
            Assert.Equal("checkpoint_restored", recovered.Outcome);
            Assert.True(coordinator.CanRunBehaviors);
            Assert.NotSame(original, coordinator.CurrentWorld);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public void InvalidQuestInsideCanonicalArtifactFailsBeforeWorldPublication(string payload)
    {
        string path = Path.Combine(Path.GetTempPath(), "skyrim-quest-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var owner = new SkyrimWorldOwnerRuntime("saved-world");
            owner.DominatusWorld.Bb.Set(new BbKey<string>("skyrim.patrol-trial.v1"), payload);
            var store = new SkyrimCheckpointStore(path);
            string invalid = store.ExportEmbedded(owner);
            Assert.Throws<InvalidDataException>(() => store.RestoreEmbedded(invalid, "new-world"));
            Assert.Empty(Directory.GetFiles(path, "*.dom"));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Theory]
    [InlineData(0x165A4, 0)]
    [InlineData(0x165A3, 4000)]
    public async Task LoadedAgentStaysDormantOutsideItsSavedRouteWithoutIssuingNativeCommands(uint cell, float x)
    {
        var owner = new SkyrimWorldOwnerRuntime("dormant");
        var saved = new OwnedNpcAgentSaveState(1, Guid.NewGuid(), "owned-patrol-v1", 0x165A3,
            new(0, 0, 0), new(256, 256, 0), 0, 9, 0, "rest", 0.5f, 1.5f, 2);
        owner.WriteOwnedAgentState(saved);
        var behavior = new OwnedNpcAgentBehavior(TimeSpan.Zero);
        behavior.ObserveLifecycle(new LifecycleObservation("load_completed", 1, 1, "saved", 10, "test"),
            new SkyrimLifecycleProcessingResult(true, "checkpoint_restored"), owner);
        var state = new SkyrimStateResult(1, "skyrim_state_result", "state", 1, "completed", null,
            true, 1, true, 0x14, null, false, null, null, false, null, null, 0x14,
            WorldReady: true, WorldPaused: false, WorldGeneration: 3,
            PlayerCellFormId: cell, PlayerPositionX: x);
        // Stream.Null has no response: attempting a native query would fail.
        var context = new SkyrimHostContext(Stream.Null, state, owner);
        for (int index = 0; index < 10; index++) await behavior.TickAsync(context, CancellationToken.None);
        Assert.Null(behavior.Status);
        Assert.Equal(saved, owner.ReadOwnedAgentState());
    }
}
