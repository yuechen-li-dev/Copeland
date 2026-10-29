using System.Numerics;
using System.IO.Pipes;
using System.Text.Json;
using Aurelian.Actuation.Host;
using Dominatus.Core.Blackboard;
using Marionette.Skyrim;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class OwnedNpcCheckpointTests
{
    private static readonly OwnedNpcIdentity FirstBody = new(3, 11, 0xFF000800, 0x73FBF);
    private static readonly OwnedNpcIdentity LoadedBody = new(5, 12, 0xFF000800, 0x73FBF);
    private static readonly SkyrimSessionId Session = new(Guid.NewGuid());

    [Fact]
    public async Task SaveBarrierFreezesBeforeRetirementAndResumesWithANewBodyAfterCommit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string pipeName = "agent-checkpoint-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        int spawns = 0;
        int requests = 0;
        ulong sequence = 0;
        bool active = false;
        OwnedNpcIdentity? identity = null;
        OwnedNpcMovement? movement = null;
        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            try
            {
                while (true)
                {
                    var request = await MarionetteWireProtocol.ReadAsync<WorldNpcRequest>(server, timeout.Token);
                    Interlocked.Increment(ref requests);
                    switch (request.MessageKind)
                    {
                        case "query_owned_npc": break;
                        case "spawn_owned_npc":
                            int lease = Interlocked.Increment(ref spawns);
                            identity = FirstBody with { LeaseId = (ulong)lease };
                            active = true;
                            movement = null;
                            break;
                        case "release_owned_npc":
                            active = false;
                            if (movement is not null) movement = movement with { MarkerActive = false, State = "interrupted" };
                            break;
                        case "move_owned_npc":
                            movement = new OwnedNpcMovement(55, "walking", "", request.TargetPositionX,
                                request.TargetPositionY, request.TargetPositionZ, 48, 300, 0, 0xFF000900, true);
                            break;
                        default: throw new InvalidOperationException(request.MessageKind);
                    }
                    WorldNpcObservation[] actors = active ? [new WorldNpcObservation(identity!.FormId,
                        identity.BaseFormId, 0x165A3, "Hunter", 0, 0, 0, 100, 100, 100, true, false)] : [];
                    string ownedState = identity is null ? "none" : active ? "live" : "released";
                    await MarionetteWireProtocol.WriteAsync(server, new WorldNpcResult(1, "world_npc_result",
                        request.RequestId, ++sequence, "completed", "", 3, active ? sequence : 0, 0, 0,
                        actors, identity, ownedState, (ulong)spawns, active ? (ulong)spawns - 1 : (ulong)spawns,
                        active ? 1U : 0U, active ? 1U : 0U, true, movement), timeout.Token);
                }
            }
            catch (EndOfStreamException) { }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }, timeout.Token);
        await client.ConnectAsync(timeout.Token);
        var owner = ReadyOwner();
        var plan = State() with { CompletedLegs = 4, Waypoint = 1 };
        owner.WriteOwnedAgentState(plan);
        var behavior = new OwnedNpcAgentBehavior(TimeSpan.Zero);
        behavior.ObserveLifecycle(new LifecycleObservation("load_completed", 1, 1, "saved", 10, "test"),
            new SkyrimLifecycleProcessingResult(true, "checkpoint_restored"), owner);
        var state = new SkyrimStateResult(1, "skyrim_state_result", "state", 1, "completed", null,
            true, 1, true, 0x14, null, false, null, null, false, null, null, 0x14,
            WorldReady: true, WorldPaused: false, WorldGeneration: 3);
        var context = new SkyrimHostContext(client, state, owner);
        try
        {
            for (int index = 0; index < 20 && behavior.Status?.State != "walking"; index++)
                await behavior.TickAsync(context, timeout.Token);
            Assert.Equal("walking", behavior.Status!.State);
            long beforeTick = behavior.Status.Tick;
            await Assert.ThrowsAsync<SkyrimWorldCommandDeferredException>(
                async () => await behavior.PrepareSaveAsync(context, timeout.Token));
            Assert.False(behavior.SavePrepared);
            var frozen = owner.ReadOwnedAgentState();
            int beforeRequests = Volatile.Read(ref requests);
            for (int index = 0; index < 10; index++) await behavior.TickAsync(context, timeout.Token);
            Assert.Equal(beforeRequests, Volatile.Read(ref requests));
            Assert.Equal(beforeTick, behavior.Status.Tick);
            await behavior.PrepareSaveAsync(context, timeout.Token);
            Assert.True(behavior.SavePrepared);
            Assert.Equal(frozen, owner.ReadOwnedAgentState());
            Assert.Equal(4, frozen!.CompletedLegs);
            behavior.ObserveLifecycle(new LifecycleObservation("save_serialized", 2, 2, "saved", 10, "test"),
                new SkyrimLifecycleProcessingResult(true, "save_checkpoint_committed"), owner);
            for (int index = 0; index < 20 && behavior.Status?.State != "walking"; index++)
                await behavior.TickAsync(context, timeout.Token);
            Assert.Equal(2, Volatile.Read(ref spawns));
            Assert.Equal(plan.AgentId, behavior.Status!.SemanticId);
            Assert.Equal(4, behavior.Status.CompletedLegs);
            Assert.Equal(1, behavior.Status.Waypoint);
        }
        finally
        {
            client.Dispose();
            timeout.Cancel();
            await serve;
        }
    }

    [Fact]
    public void RestSnapshotPreservesSemanticIdentityRouteProgressAndOnlyRemainingTime()
    {
        var original = new OwnedNpcAgentRuntime(Fact(FirstBody, 1), restSeconds: 2);
        OwnedNpcAgentIntent walk = NextIntent(original);
        original.Receive(new OwnedNpcAgentReceipt(walk.Id, FirstBody, true, 55, ""));
        original.Observe(Fact(FirstBody, 2) with
        {
            Position = walk.Target,
            Movement = new OwnedNpcMovement(55, "arrived", "", walk.Target.X, walk.Target.Y,
                walk.Target.Z, 48, 0, 1, 0xFF000900, true),
        });
        for (int index = 0; index < 8; index++) original.Tick(0);
        original.Tick(0.5f);
        OwnedNpcAgentSaveState saved = original.Capture(0x165A3);
        Assert.Equal("rest", saved.Mode);
        Assert.InRange(saved.RemainingWaitSeconds, 1.49f, 1.51f);
        var restored = new OwnedNpcAgentRuntime(Fact(LoadedBody, 1), saved: saved);
        Assert.Equal(original.SemanticId, restored.SemanticId);
        Assert.Equal(1, restored.Status.CompletedLegs);
        Assert.Equal(1, restored.Status.Waypoint);
        for (int index = 0; index < 12; index++)
        {
            restored.Tick(0.1f);
            Assert.Null(restored.TakeIntent());
        }
        OwnedNpcAgentIntent resumed = NextIntent(restored);
        Assert.Equal(LoadedBody, resumed.Identity);
        Assert.Equal(Vector3.Zero, resumed.Target);
        Assert.Equal(0UL, restored.Status.MovementId);
    }

    [Fact]
    public void InFlightOperationBecomesAnUnissuedGoalWithFreshCorrelation()
    {
        var original = new OwnedNpcAgentRuntime(Fact(FirstBody, 1));
        OwnedNpcAgentIntent old = NextIntent(original);
        original.Receive(new OwnedNpcAgentReceipt(old.Id, FirstBody, true, 55, ""));
        original.Observe(Fact(FirstBody, 2) with
        {
            Movement = new OwnedNpcMovement(55, "walking", "", old.Target.X, old.Target.Y,
                old.Target.Z, 48, 200, 1, 0xFF000900, true),
        });
        original.Tick(0.1f);
        var saved = original.Capture(0x165A3);
        Assert.Equal("patrol", saved.Mode);
        var restored = new OwnedNpcAgentRuntime(Fact(LoadedBody, 1), saved: saved);
        var next = NextIntent(restored);
        Assert.Equal(old.Target, next.Target);
        Assert.Equal(LoadedBody, next.Identity);
        Assert.Equal(0, restored.Status.CompletedLegs);
        Assert.Throws<InvalidDataException>(() => restored.Receive(
            new OwnedNpcAgentReceipt(old.Id, FirstBody, true, 55, "")));
    }

    [Fact]
    public void RecoveringAndStoppedModesSurviveColdEntry()
    {
        var state = State() with { Mode = "recover", Failures = 1, RemainingWaitSeconds = 0.6f };
        var recovered = new OwnedNpcAgentRuntime(Fact(LoadedBody, 1), saved: state);
        for (int index = 0; index < 4; index++)
        {
            recovered.Tick(0.1f);
            Assert.Null(recovered.TakeIntent());
        }
        Assert.Equal(1, recovered.Status.ConsecutiveFailures);
        Assert.Equal(OwnedNpcAgentAction.Walk, NextIntent(recovered).Action);
        var stopped = new OwnedNpcAgentRuntime(Fact(LoadedBody, 1),
            saved: state with { Mode = "stopped", RemainingWaitSeconds = 0 });
        for (int index = 0; index < 30; index++)
        {
            stopped.Tick(0.1f);
            Assert.Null(stopped.TakeIntent());
        }
        Assert.Equal("stopped", stopped.Status.State);
    }

    [Fact]
    public void CanonicalDomArtifactRewindsFutureStateAcrossStoreAndSessionRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "owned-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            var owner = ReadyOwner();
            var savedA = State() with { CompletedLegs = 4, Waypoint = 1 };
            owner.WriteOwnedAgentState(savedA);
            var first = new SkyrimCheckpointStore(directory);
            Assert.True(first.Capture(owner, Save("Agent-A", 10, 1), new BodyBindingRegistry()).Completed);
            owner.WriteOwnedAgentState(savedA with { CompletedLegs = 12, Waypoint = 0 });
            Assert.True(first.Capture(owner, Save("Agent-B", 20, 2), new BodyBindingRegistry()).Completed);
            var restarted = new SkyrimCheckpointStore(directory);
            var result = restarted.Restore(Save("Agent-A", 10, 3), "new-controller-session");
            Assert.True(result.Completed, result.FailureReason);
            Assert.Equal(savedA, result.RestoredWorld!.ReadOwnedAgentState());
            Assert.False(restarted.ReadEntries().Single(entry => entry.Save.SaveName == "Agent-B").ActiveLineage);
            Assert.Equal(12, owner.ReadOwnedAgentState()!.CompletedLegs);
            Assert.Equal(2, Directory.GetFiles(directory, "*.dom").Length);
            string raw = JsonSerializer.Serialize(savedA, OwnedNpcCheckpointJsonContext.Default.OwnedNpcAgentSaveState);
            Assert.DoesNotContain("lease", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("movementId", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("worldGeneration", raw, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"version\":99}")]
    public void MalformedAgentPayloadCannotRestoreAsAnEmptyOrFreshAgent(string payload)
    {
        string directory = Path.Combine(Path.GetTempPath(), "owned-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var owner = ReadyOwner();
            owner.DominatusWorld.Bb.Set(new BbKey<string>("skyrim.owned-agent.semantic.v1"), payload);
            var store = new SkyrimCheckpointStore(directory);
            Assert.True(store.Capture(owner, Save("Invalid", 10, 1), new BodyBindingRegistry()).Completed);
            var restored = store.Restore(Save("Invalid", 10, 2), "restored");
            Assert.Equal(SkyrimCheckpointStatus.CheckpointCorrupt, restored.Status);
            Assert.Null(restored.RestoredWorld);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void InvalidVersionDefinitionAndRouteAreRejectedBeforeUse()
    {
        Assert.True(State().CanMaterialize(0x165A3, Vector3.Zero));
        Assert.False(State().CanMaterialize(0x165A4, Vector3.Zero));
        Assert.False(State().CanMaterialize(0x165A3, new Vector3(4000, 0, 0)));
        Assert.Throws<InvalidDataException>(() => (State() with { Version = 2 }).Validate());
        Assert.Throws<InvalidDataException>(() => (State() with { Definition = "other-agent" }).Validate());
        Assert.Throws<InvalidDataException>(() => (State() with { Waypoint = 2 }).Validate());
        Assert.Throws<InvalidDataException>(() => (State() with { RemainingWaitSeconds = float.NaN }).Validate());
        Assert.Throws<InvalidDataException>(() => new OwnedNpcAgentRuntime(
            Fact(LoadedBody, 1) with { Position = new Vector3(4000, 0, 0) }, saved: State()));
    }

    private static OwnedNpcAgentFact Fact(OwnedNpcIdentity identity, ulong sequence)
    {
        return new OwnedNpcAgentFact(identity, sequence, true, Vector3.Zero, null);
    }

    private static OwnedNpcAgentSaveState State()
    {
        return new OwnedNpcAgentSaveState(1, Guid.NewGuid(), "owned-patrol-v1", 0x165A3,
            new OwnedAgentPoint(0, 0, 0), new OwnedAgentPoint(256, 256, 0), 0, 0, 0, "patrol", 0, 1.5f, 2);
    }

    private static SkyrimSaveIdentity Save(string name, double days, long operation)
    {
        return new SkyrimSaveIdentity(name, new SkyrimTimelineStamp(Session,
            new SkyrimGameTimestamp(days), operation), operation);
    }

    private static SkyrimWorldOwnerRuntime ReadyOwner()
    {
        var owner = new SkyrimWorldOwnerRuntime("owned-checkpoint");
        owner.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
        for (int index = 0; index < 6; index++) owner.Tick();
        owner.Post(new SkyrimWorldFact(SkyrimWorldFactKind.WorldReady, 2,
            new SkyrimTimelineStamp(Session, new SkyrimGameTimestamp(10), 1)));
        for (int index = 0; index < 6; index++) owner.Tick();
        return owner;
    }

    private static OwnedNpcAgentIntent NextIntent(OwnedNpcAgentRuntime runtime)
    {
        for (int index = 0; index < 100; index++)
        {
            runtime.Tick(0.1f);
            if (runtime.TakeIntent() is { } intent) return intent;
        }
        throw new InvalidOperationException("resumed_agent_produced_no_intent");
    }
}
