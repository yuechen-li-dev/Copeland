using Aurelian.Actuation.Host;
using Marionette.Skyrim;
using Dominatus.Core.Runtime;
using System.Buffers.Binary;
using System.Text;
using Xunit;
using AurelianAgentId = Aurelian.Actuation.Host.AgentId;

namespace Marionette.Skyrim.App.Tests;

public sealed class SkyrimLiveLifecycleTests
{
    private static readonly SkyrimSessionId Session = new(
        Guid.Parse("f65aca2d-34d7-443c-b57c-aa1a95d105d7"));

    [Fact]
    public void SaveAThenBLoadA_RestoresHistoricalWorldAndRematerializesPlacedAgent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-m4a-{Guid.NewGuid():N}");
        try
        {
            SkyrimWorldOwnerRuntime owner = ReadyOwner();
            var originA = new SkyrimPlacedActorOrigin("Example.esm", 0x100);
            PostBody(owner, 3, originA, "body-a-old");
            AurelianAgentId agentA = owner.Registry.Find(originA)!.Id;
            var store = new SkyrimCheckpointStore(directory);
            var coordinator = new SkyrimLiveLifecycleCoordinator(
                Session,
                owner,
                store,
                new BodyBindingRegistry(),
                "restored-session");

            Assert.True(coordinator.Process(Observation("save_started", 1, 1, "AURELIAN_M4A_A", 10)).Accepted);
            SkyrimLifecycleProcessingResult saveA = coordinator.Process(
                Observation("save_serialized", 2, 1, "AURELIAN_M4A_A", 10));
            Assert.True(saveA.Accepted);

            var originB = new SkyrimPlacedActorOrigin("Example.esm", 0x200);
            PostBody(owner, 10, originB, "body-b");
            Assert.True(coordinator.Process(Observation("save_started", 3, 2, "AURELIAN_M4A_B", 20)).Accepted);
            Assert.True(coordinator.Process(Observation("save_serialized", 4, 2, "AURELIAN_M4A_B", 20)).Accepted);

            Assert.True(coordinator.Process(Observation("load_started", 5, 3, "AURELIAN_M4A_A", 20)).Accepted);
            SkyrimLifecycleProcessingResult loaded = coordinator.Process(
                Observation("load_completed", 6, 3, "AURELIAN_M4A_A", 10));

            Assert.True(loaded.Accepted, loaded.Outcome);
            Assert.NotSame(owner, coordinator.CurrentWorld);
            Assert.Equal(agentA, coordinator.CurrentWorld.Registry.Find(originA)!.Id);
            Assert.Null(coordinator.CurrentWorld.Registry.Find(originB));
            Assert.Null(coordinator.CurrentWorld.Registry.CurrentBody(originA));
            Assert.Equal(SkyrimWorldOwnerState.WorldReady, coordinator.CurrentWorld.State);

            PostBody(coordinator.CurrentWorld, 20, originA, "body-a-current");
            Assert.Equal(agentA, coordinator.CurrentWorld.Registry.Find(originA)!.Id);
            Assert.Equal(
                new BodyId("body-a-current"),
                coordinator.CurrentWorld.Registry.CurrentBody(originA));
            EventCursor restoredCursor = default;
            EventCursor rebasedCursor = default;
            EventCursor bodyCursor = default;
            Assert.True(coordinator.CurrentWorld.TryConsume(ref restoredCursor, out SkyrimWorldRestored _));
            Assert.True(coordinator.CurrentWorld.TryConsume(ref rebasedCursor, out SkyrimTimelineRebased _));
            Assert.True(coordinator.CurrentWorld.TryConsume(ref bodyCursor, out SkyrimBodyLoaded rematerialized));
            Assert.True(rematerialized.Rematerialized);
            Assert.Equal(BodyBindingState.Unbound, rematerialized.Body.BindingState);

            SkyrimCheckpointIndexEntry checkpointB = store.ReadEntries().Single(
                entry => entry.Save.SaveName == "AURELIAN_M4A_B");
            Assert.False(checkpointB.ActiveLineage);
            Assert.True(File.Exists(Path.Combine(directory, checkpointB.ArtifactFileName)));
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
    public void FailedLoadDoesNotRestoreAndDuplicateFactsAreIdempotent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-m4a-{Guid.NewGuid():N}");
        try
        {
            SkyrimWorldOwnerRuntime owner = ReadyOwner();
            var coordinator = new SkyrimLiveLifecycleCoordinator(
                Session,
                owner,
                new SkyrimCheckpointStore(directory),
                new BodyBindingRegistry(),
                "restore");

            Assert.True(coordinator.Process(Observation("load_started", 1, 8, "BrokenSave", 10)).Accepted);
            Assert.True(coordinator.Process(Observation("load_failed", 2, 8, "BrokenSave", 10)).Accepted);
            Assert.Same(owner, coordinator.CurrentWorld);
            Assert.Equal(SkyrimWorldOwnerState.WorldReady, owner.State);
            Assert.False(coordinator.Process(Observation("load_failed", 2, 8, "BrokenSave", 10)).Accepted);
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
    public void BaselineLoadWithoutCheckpointRemainsWorldReady()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-m4a-{Guid.NewGuid():N}");
        try
        {
            SkyrimWorldOwnerRuntime owner = ReadyOwner();
            var coordinator = new SkyrimLiveLifecycleCoordinator(
                Session,
                owner,
                new SkyrimCheckpointStore(directory),
                new BodyBindingRegistry(),
                "restore");

            Assert.True(coordinator.Process(Observation("load_started", 1, 8, "Fixture", 10)).Accepted);
            SkyrimLifecycleProcessingResult completed = coordinator.Process(
                Observation("load_completed", 2, 8, "Fixture", 10));

            Assert.True(completed.Accepted);
            Assert.Equal("load_completed_without_checkpoint", completed.Outcome);
            Assert.Equal(SkyrimCheckpointStatus.CheckpointUnavailable, completed.Checkpoint!.Status);
            Assert.Same(owner, coordinator.CurrentWorld);
            Assert.Equal(SkyrimWorldOwnerState.WorldReady, owner.State);
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
    public void ActiveBindingMakesLoadRestorationRequired()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aurelian-m4a-{Guid.NewGuid():N}");
        try
        {
            var bindings = new BodyBindingRegistry();
            Assert.True(bindings.BeginBinding(
                new AurelianAgentId(Guid.NewGuid()),
                new BodyId("active-body"),
                BodyBindingKind.ExclusiveControl,
                1).Accepted);
            var coordinator = new SkyrimLiveLifecycleCoordinator(
                Session,
                ReadyOwner(),
                new SkyrimCheckpointStore(directory),
                bindings,
                "restore");

            SkyrimLifecycleProcessingResult result = coordinator.Process(
                Observation("load_started", 1, 9, "Save", 10));

            Assert.False(result.Accepted);
            Assert.True(coordinator.CurrentWorld.RestorationIsRequired);
            Assert.False(coordinator.CurrentWorld.CanIssueBodyCommands);
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
    public async Task LifecycleWirePayloadIsAdditiveAndMalformedPayloadFailsClearly()
    {
        var result = new LifecycleObservationsResult(
            1,
            "lifecycle_observations_result",
            "request",
            2,
            "completed",
            [Observation("load_completed", 7, 3, "Save A", 12.5)]);
        byte[] frame = MarionetteWireProtocol.Encode(result);
        using var stream = new MemoryStream(frame);
        LifecycleObservationsResult parsed = await MarionetteWireProtocol.ReadAsync<LifecycleObservationsResult>(
            stream,
            CancellationToken.None);
        Assert.Equal(3UL, parsed.Observations[0].OperationId);
        Assert.Equal("Save A", parsed.Observations[0].SaveName);

        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":1,\"messageKind\":\"lifecycle_observations_result\"," +
            "\"requestId\":\"request\",\"serverSequence\":2,\"status\":\"completed\"," +
            "\"observations\":\"wrong\"}");
        byte[] malformed = new byte[payload.Length + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(malformed, (uint)payload.Length);
        payload.CopyTo(malformed, sizeof(uint));
        using var malformedStream = new MemoryStream(malformed);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await MarionetteWireProtocol.ReadAsync<LifecycleObservationsResult>(
                malformedStream,
                CancellationToken.None));
    }

    private static LifecycleObservation Observation(
        string kind,
        ulong sequence,
        ulong operationId,
        string? saveName,
        double? gameDays) => new(
            kind,
            sequence,
            operationId,
            saveName,
            gameDays,
            kind switch
            {
                "save_started" => "SKSE::kSaveGame",
                "save_serialized" => "SKSE::SerializationInterface::SaveCallback",
                "load_started" => "SKSE::kPreLoadGame",
                _ => "SKSE::kPostLoadGame",
            });

    private static SkyrimWorldOwnerRuntime ReadyOwner()
    {
        var owner = new SkyrimWorldOwnerRuntime("session");
        owner.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
        Tick(owner);
        owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.WorldReady,
            2,
            new SkyrimTimelineStamp(Session, new SkyrimGameTimestamp(10), 1)));
        Tick(owner);
        return owner;
    }

    private static void PostBody(
        SkyrimWorldOwnerRuntime owner,
        long sequence,
        SkyrimPlacedActorOrigin placed,
        string bodyId)
    {
        owner.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.BodyLoaded,
            sequence,
            Body: new BodyObservation(
                new BodyId(bodyId),
                true,
                false,
                new HostPosition3(1, 2, 3),
                new BodyCapabilities(true, false, false, false, true, true),
                BodyBindingState.Unbound,
                null,
                1,
                1),
            Origin: SkyrimActorOrigin.ForPlaced(placed),
            ImportedData: new ImportedNpcData(
                new IdentityProfile(bodyId, "npc"),
                new BodyProfile(true, false, false),
                SelectionProfile.ImportedDefault)));
        Tick(owner);
    }

    private static void Tick(SkyrimWorldOwnerRuntime owner)
    {
        for (int index = 0; index < 6; index++)
        {
            owner.Tick();
        }
    }
}
