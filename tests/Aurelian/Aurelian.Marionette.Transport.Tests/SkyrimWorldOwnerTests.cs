using Aurelian.Actuation.Host;
using Dominatus.Core;
using Dominatus.Core.Runtime;
using Dominatus.Core.Persistence;
using Xunit;
using AurelianAgentId = Aurelian.Actuation.Host.AgentId;

namespace Aurelian.Marionette.Transport.Tests;

public sealed class SkyrimWorldOwnerTests
{
    [Fact]
    public void OrderedLifecycle_GatesCommandsAndPublishesReadyOnce()
    {
        var owner = new SkyrimWorldOwnerRuntime("session");
        SkyrimTimelineStamp timeline = Timeline(10, 1);

        Assert.False(owner.CanIssueBodyCommands);
        Assert.True(owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.BackendConnected,
            Sequence: 1)));
        Tick(owner);
        Assert.Equal(SkyrimWorldOwnerState.AwaitingWorld, owner.State);

        Assert.True(owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.WorldReady,
            Sequence: 2,
            Timeline: timeline)));
        Assert.True(owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.WorldReady,
            Sequence: 2,
            Timeline: timeline)));
        Tick(owner);

        Assert.Equal(SkyrimWorldOwnerState.WorldReady, owner.State);
        Assert.True(owner.CanIssueBodyCommands);
        EventCursor cursor = default;
        Assert.True(owner.TryConsume(ref cursor, out SkyrimWorldReady ready));
        Assert.Equal(timeline, ready.Timeline);
        Assert.False(owner.TryConsume(ref cursor, out SkyrimWorldReady _));

        owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.BackendDisconnected,
            Sequence: 3,
            Reason: "pipe_closed"));
        Tick(owner);
        Assert.Equal(SkyrimWorldOwnerState.Disconnected, owner.State);
        Assert.False(owner.CanIssueBodyCommands);
        Assert.Equal(10, owner.FlowInspection.States.Count);
    }

    [Fact]
    public void BodyLossAndRematerialization_PreserveSemanticAgent()
    {
        var owner = ReadyOwner();
        SkyrimActorOrigin origin = SkyrimActorOrigin.ForPlaced(
            new SkyrimPlacedActorOrigin("Example.esp", 0x1234));
        BodyObservation firstBody = Body("body-runtime-01", 1);
        owner.Post(BodyFact(3, firstBody, origin));
        Tick(owner);
        ImportedNpcAgent first = owner.Registry.Find(origin.Placed!.Value)!;

        owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.BodyLost,
            Sequence: 4,
            Body: firstBody));
        Tick(owner);
        Assert.True(owner.Registry.IsBodyLost(firstBody.Id));

        BodyObservation secondBody = Body("body-runtime-09", 2);
        owner.Post(BodyFact(5, secondBody, origin));
        Tick(owner);
        ImportedNpcAgent second = owner.Registry.Find(origin.Placed.Value)!;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(secondBody.Id, owner.Registry.CurrentBody(origin.Placed.Value));
        EventCursor cursor = default;
        Assert.True(owner.TryConsume(ref cursor, out SkyrimBodyLoaded _));
        Assert.True(owner.TryConsume(ref cursor, out SkyrimBodyLoaded rematerialized));
        Assert.True(rematerialized.Rematerialized);
    }

    [Fact]
    public void EarlierGameDays_DetectRollbackDespiteSequenceReset()
    {
        var owner = ReadyOwner(gameDays: 20);
        SkyrimSaveIdentity earlier = Save("Save A", 10, sequence: 0);
        owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.SaveLoading,
            Sequence: 3,
            Save: earlier));
        Tick(owner);
        owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.SaveLoaded,
            Sequence: 4,
            Save: earlier));
        Tick(owner);

        Assert.Equal(SkyrimWorldOwnerState.RollbackDetected, owner.State);
        EventCursor cursor = default;
        Assert.True(owner.TryConsume(ref cursor, out SkyrimRollbackDetected rollback));
        Assert.Equal(20, rollback.Previous.GameTime.GameDays);
        Assert.Equal(10, rollback.Loaded.GameTime.GameDays);
    }

    [Fact]
    public void Checkpoint_UsesDominatusDom1AndFreshRestoreStartsUnbound()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-skyrim-{Guid.NewGuid():N}");
        try
        {
            var owner = ReadyOwner();
            var origin = new SkyrimPlacedActorOrigin("Example.esl", 0xabc);
            owner.Post(BodyFact(3, Body("runtime-fe001abc", 1), SkyrimActorOrigin.ForPlaced(origin)));
            Tick(owner);
            AurelianAgentId semanticId = owner.Registry.Find(origin)!.Id;
            var store = new SkyrimCheckpointStore(directory);

            SkyrimCheckpointResult captured = store.Capture(
                owner,
                Save("Save A", 10, 1),
                new BodyBindingRegistry());

            Assert.True(captured.Completed, captured.FailureReason);
            string artifact = Path.Combine(directory, captured.Entry!.ArtifactFileName);
            Assert.Equal("DOM1", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(artifact), 0, 4));
            string index = File.ReadAllText(Path.Combine(directory, "skyrim-checkpoints.index.json"));
            Assert.DoesNotContain("Fixture NPC", index, StringComparison.Ordinal);

            SkyrimCheckpointResult restored = store.Restore(Save("Save A", 10, 1), "fresh-session");

            Assert.True(restored.Completed, restored.FailureReason);
            Assert.NotSame(owner, restored.RestoredWorld);
            ImportedNpcAgent restoredAgent = restored.RestoredWorld!.Registry.Find(origin)!;
            Assert.Equal(semanticId, restoredAgent.Id);
            Assert.Null(restored.RestoredWorld.Registry.CurrentBody(origin));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ActiveBindingBlocksCheckpointAndRollbackSelectsHistoricalArtifact()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-skyrim-{Guid.NewGuid():N}");
        try
        {
            var owner = ReadyOwner();
            var bindings = new BodyBindingRegistry();
            BodyObservation body = Body("body", 1);
            var agent = new AurelianAgentId(Guid.NewGuid());
            Assert.True(bindings.BeginBinding(
                agent,
                body.Id,
                BodyBindingKind.ExclusiveControl,
                body.Generation).Accepted);
            var store = new SkyrimCheckpointStore(directory);

            SkyrimCheckpointResult blocked = store.Capture(owner, Save("Save A", 10, 1), bindings);
            Assert.Equal(SkyrimCheckpointStatus.ActiveBinding, blocked.Status);

            Assert.True(bindings.FailBinding(agent, body.Id, restoreRequired: false).Accepted);
            SkyrimCheckpointResult checkpointA = store.Capture(owner, Save("Save A", 10, 1), bindings);
            owner.Post(new SkyrimWorldFact(
                SkyrimWorldFactKind.WorldReady,
                Sequence: 3,
                Timeline: Timeline(20, 2)));
            Tick(owner);
            SkyrimCheckpointResult checkpointB = store.Capture(owner, Save("Save B", 20, 2), bindings);

            SkyrimCheckpointResult restored = store.Restore(Save("Save A", 10, 1), "restored");

            Assert.Equal(checkpointA.Entry!.CheckpointArtifactId, restored.Entry!.CheckpointArtifactId);
            SkyrimCheckpointIndexEntry b = store.ReadEntries().Single(
                entry => entry.CheckpointArtifactId == checkpointB.Entry!.CheckpointArtifactId);
            Assert.False(
                b.ActiveLineage,
                System.Text.Json.JsonSerializer.Serialize(store.ReadEntries()));
            Assert.True(File.Exists(Path.Combine(directory, b.ArtifactFileName)));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MissingCorruptAndVersionMismatchedCheckpointsAreExplicit()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-skyrim-{Guid.NewGuid():N}");
        try
        {
            var store = new SkyrimCheckpointStore(directory);
            Assert.Equal(
                SkyrimCheckpointStatus.CheckpointUnavailable,
                store.Restore(Save("missing", 1, 1), "restore").Status);

            SkyrimCheckpointResult captured = store.Capture(
                ReadyOwner(),
                Save("Save A", 10, 1),
                new BodyBindingRegistry());
            string path = Path.Combine(directory, captured.Entry!.ArtifactFileName);
            File.WriteAllText(path, "not-a-dominatus-checkpoint");
            Assert.Equal(
                SkyrimCheckpointStatus.CheckpointCorrupt,
                store.Restore(Save("Save A", 10, 1), "restore").Status);

            captured = store.Capture(
                ReadyOwner(),
                Save("Save B", 20, 2),
                new BodyBindingRegistry());
            path = Path.Combine(directory, captured.Entry!.ArtifactFileName);
            List<SaveChunk> chunks = SaveFile.Read(path);
            chunks = chunks.Select(chunk => chunk.Id == ChunkId.Meta
                ? new SaveChunk(
                    ChunkId.Meta,
                    System.Text.Encoding.UTF8.GetBytes(
                        "{\"format\":\"dominatus-save\",\"v\":999,\"checkpointVersion\":1}"))
                : chunk).ToList();
            SaveFile.Write(path, chunks);
            Assert.Equal(
                SkyrimCheckpointStatus.VersionMismatch,
                store.Restore(Save("Save B", 20, 2), "restore").Status);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void OverwrittenSaveNameRequiresMatchingTimelineRevision()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-skyrim-{Guid.NewGuid():N}");
        try
        {
            var owner = ReadyOwner();
            var store = new SkyrimCheckpointStore(directory);
            var bindings = new BodyBindingRegistry();
            SkyrimCheckpointResult first = store.Capture(owner, Save("SaveSlot", 10, 1), bindings);

            var laterOrigin = new SkyrimPlacedActorOrigin("Later.esp", 0x321);
            owner.Post(BodyFact(3, Body("later-body", 1), SkyrimActorOrigin.ForPlaced(laterOrigin)));
            Tick(owner);
            owner.Post(new SkyrimWorldFact(
                SkyrimWorldFactKind.WorldReady,
                4,
                Timeline(20, 2)));
            Tick(owner);
            SkyrimCheckpointResult overwritten = store.Capture(owner, Save("SaveSlot", 20, 2), bindings);

            SkyrimCheckpointResult restored = store.Restore(Save("SaveSlot", 10, 1), "restore");

            Assert.Equal(first.Entry!.CheckpointArtifactId, restored.Entry!.CheckpointArtifactId);
            Assert.NotEqual(first.Entry.CheckpointArtifactId, overwritten.Entry!.CheckpointArtifactId);
            Assert.Null(restored.RestoredWorld!.Registry.Find(laterOrigin));
            Assert.False(store.ReadEntries().Single(
                entry => entry.CheckpointArtifactId == overwritten.Entry.CheckpointArtifactId).ActiveLineage);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static SkyrimWorldOwnerRuntime ReadyOwner(double gameDays = 10)
    {
        var owner = new SkyrimWorldOwnerRuntime("session");
        owner.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
        Tick(owner);
        owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.WorldReady,
            2,
            Timeline(gameDays, 1)));
        Tick(owner);
        return owner;
    }

    private static void Tick(SkyrimWorldOwnerRuntime owner)
    {
        for (int index = 0; index < 6; index++)
        {
            owner.Tick();
        }
    }

    private static SkyrimWorldFact BodyFact(
        long sequence,
        BodyObservation body,
        SkyrimActorOrigin origin) => new(
            SkyrimWorldFactKind.BodyLoaded,
            sequence,
            Body: body,
            Origin: origin,
            ImportedData: Data());

    private static SkyrimTimelineStamp Timeline(double gameDays, long sequence) => new(
        new SkyrimSessionId(Guid.Parse("ed7a9e48-5fd3-48fc-92e8-10eb96a0f5ec")),
        new SkyrimGameTimestamp(gameDays),
        sequence);

    private static SkyrimSaveIdentity Save(string name, double gameDays, long sequence) =>
        new(name, Timeline(gameDays, sequence));

    private static ImportedNpcData Data() => new(
        new IdentityProfile("Fixture NPC", "humanoid-corpse"),
        new BodyProfile(true, false, false),
        SelectionProfile.ImportedDefault);

    private static BodyObservation Body(string id, ulong generation) => new(
        new BodyId(id),
        IsLoaded: true,
        IsAlive: false,
        new HostPosition3(1, 2, 3),
        new BodyCapabilities(true, false, false, false, true, true),
        BodyBindingState.Unbound,
        BoundAgent: null,
        generation,
        Sequence: generation);
}
