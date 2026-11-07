using System.IO.Pipes;
using System.Numerics;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class OwnedNpcAgentHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealHostGatesHumanControlAndRebindsOnlyInANewWorld(bool diesOutsideRoute)
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "owned-agent-" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var config = new LocalTransportConfig("agent-" + Guid.NewGuid().ToString("N"), "test", "test",
            CheckpointDirectory: directory);
        using var server = new NamedPipeServerStream(config.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var behavior = new OwnedNpcAgentBehavior(TimeSpan.Zero);
        ulong sequence = 0;
        ulong observation = 0;
        ulong generation = 1;
        int stateTicks = 0;
        int spawns = 0;
        int moves = 0;
        int secondWorldMoves = 0;
        int deathReleases = 0;
        int arrived = 0;
        bool paused = false;
        bool manuallyReleased = false;
        bool sawStopped = false;
        bool active = false;
        bool sawHumanControl = false;
        bool sawOutsideRegion = false;
        long? suspendedTick = null;
        long? outsideRegionTick = null;
        Guid? controlledSemanticId = null;
        int failuresAtTakeover = 0;
        bool sawSemanticResume = false;
        OwnedNpcIdentity? identity = null;
        OwnedNpcMovement? movement = null;
        Vector3 position = Vector3.Zero;
        Vector3 target = Vector3.Zero;

        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await MarionetteWireProtocol.ReadAsync<ClientHello>(server, timeout.Token);
            await MarionetteWireProtocol.WriteAsync(server, new ServerHello(1, "server_hello", true,
                "agent-session", "test", ["query_world_npcs", "query_lifecycle_observations"], null), timeout.Token);
            while (true)
            {
                WorldNpcRequest request = await MarionetteWireProtocol.ReadAsync<WorldNpcRequest>(server, timeout.Token);
                if (request.MessageKind == "query_skyrim_state")
                {
                    stateTicks++;
                    paused = stateTicks is >= 2 and <= 5;
                    if (behavior.Status?.State == "stopped" && generation == 1)
                    {
                        sawStopped = true;
                        if (stateTicks > 75)
                        {
                            generation = 2;
                            identity = null;
                            movement = null;
                            active = false;
                            position = new Vector3(100, 100, 0);
                        }
                    }
                    if (generation == 2 && secondWorldMoves > 0) break;
                    await MarionetteWireProtocol.WriteAsync(server, new SkyrimStateResult(1, "skyrim_state_result",
                        request.RequestId, ++sequence, "completed", null, true, sequence, true, 0x14,
                        null, false, null, null, false, null, null, 0x14, GameTimeDays: 10,
                        WorldReady: true, WorldPaused: paused, WorldGeneration: generation,
                        PlayerCellFormId: stateTicks is >= 15 and <= 18 ? 0x1A274U : 0x165A3U), timeout.Token);
                    continue;
                }
                if (request.MessageKind == "query_lifecycle_observations")
                {
                    await MarionetteWireProtocol.WriteAsync(server, new LifecycleObservationsResult(1,
                        "lifecycle_observations_result", request.RequestId, ++sequence, "completed", []), timeout.Token);
                    continue;
                }
                Assert.False(paused);
                if (!diesOutsideRoute && stateTicks is >= 16 and <= 18)
                {
                    sawOutsideRegion = true;
                    outsideRegionTick ??= behavior.Status!.Tick;
                    Assert.Equal(outsideRegionTick.Value, behavior.Status!.Tick);
                    Assert.Equal("query_owned_npc", request.MessageKind);
                }
                if (!diesOutsideRoute && stateTicks >= 20 && generation == 1 && !sawSemanticResume)
                {
                    sawSemanticResume = true;
                    Assert.Equal(controlledSemanticId, behavior.Status!.SemanticId);
                    Assert.Equal(failuresAtTakeover, behavior.Status.ConsecutiveFailures);
                }
                bool humanControl = stateTicks is >= 10 and <= 14 && identity is not null;
                if (humanControl && behavior.HumanControlled)
                {
                    sawHumanControl = true;
                    suspendedTick ??= behavior.Status!.Tick;
                    if (controlledSemanticId is null)
                    {
                        controlledSemanticId = behavior.Status!.SemanticId;
                        failuresAtTakeover = behavior.Status.ConsecutiveFailures;
                    }
                    Assert.Equal(suspendedTick.Value, behavior.Status!.Tick);
                    Assert.Equal("query_owned_npc", request.MessageKind);
                }
                if (request.MessageKind == "spawn_owned_npc")
                {
                    spawns++;
                    Assert.Equal((int)generation, spawns);
                    identity = new OwnedNpcIdentity(generation, (ulong)spawns, 0xFF000800, 0x73FBF);
                    active = true;
                }
                else if (request.MessageKind == "move_owned_npc")
                {
                    Assert.Equal(identity!.LeaseId, request.ExpectedOwnedLeaseId);
                    Assert.Equal(generation, request.ExpectedWorldGeneration);
                    Assert.Equal(observation, request.ExpectedObservationSequence);
                    Assert.False(manuallyReleased && generation == 1);
                    moves++;
                    if (generation == 2) secondWorldMoves++;
                    target = new Vector3(request.TargetPositionX, request.TargetPositionY, request.TargetPositionZ);
                    // The second request models a native stopping receipt.
                    // Acceptance still needs a later observed arrival.
                    string acceptedState = moves == 2 ? "stopping" : "walking";
                    movement = new OwnedNpcMovement((ulong)moves, acceptedState, "", target.X, target.Y, target.Z,
                        request.StoppingDistance, Vector3.Distance(position, target), 0, 0xFF000900, true);
                }
                else if (request.MessageKind == "release_owned_npc")
                {
                    Assert.True(diesOutsideRoute);
                    Assert.Equal(1UL, generation);
                    deathReleases++;
                    active = false;
                    manuallyReleased = true;
                    movement = null;
                }
                else
                {
                    Assert.Equal("query_owned_npc", request.MessageKind);
                    if (movement?.State is "walking" or "stopping")
                    {
                        Vector3 direction = target - position;
                        if (direction.Length() > 0) position += Vector3.Normalize(direction) * Math.Min(direction.Length(), 80);
                        float distance = Vector3.Distance(position, target);
                        movement = movement with { Distance = distance };
                        if (distance <= movement.StoppingDistance)
                        {
                            movement = movement with { State = "arrived" };
                            arrived++;
                        }
                    }
                    if (!diesOutsideRoute && arrived >= 2 && !manuallyReleased && behavior.Status?.CompletedLegs >= 2)
                    {
                        manuallyReleased = true;
                        active = false;
                        movement = movement! with { MarkerActive = false };
                    }
                }
                bool moving = movement?.State == "walking";
                bool deadOutsideRoute = diesOutsideRoute && generation == 1 && stateTicks >= 15;
                uint actorCell = diesOutsideRoute && generation == 1 && stateTicks >= 12 ? 0x1A274U : 0x165A3U;
                WorldNpcObservation[] npcs = active ? [new WorldNpcObservation(identity!.FormId,
                    identity.BaseFormId, actorCell, "Hunter", position.X, position.Y, position.Z, 100,
                    100, 100, true, deadOutsideRoute, moving, moving, true, moving ? 80 : 0)] : [];
                string ownedState = identity is null ? "none" : active ? "live" : "released";
                await MarionetteWireProtocol.WriteAsync(server, new WorldNpcResult(1, "world_npc_result",
                    request.RequestId, ++sequence, "completed", "", generation, active ? ++observation : 0, 0, 0,
                    npcs, identity, ownedState, (ulong)spawns, active ? (ulong)spawns - 1 : (ulong)spawns,
                    active ? 1U : 0U, active ? 1U : 0U, true, movement,
                    BodyControl: humanControl ? new BodyControlObservation(7, generation, identity!.FormId, Phase: "active") : null), timeout.Token);
            }
            server.Disconnect();
        }, timeout.Token);
        try
        {
            var client = new MarionetteTransportClient(config);
            await client.RunWorldHostAsync([behavior], timeout.Token);
            await serve;
            Assert.True(sawStopped);
            Assert.True(sawHumanControl);
            Assert.Equal(!diesOutsideRoute, sawOutsideRegion);
            Assert.Equal(!diesOutsideRoute, sawSemanticResume);
            Assert.Equal(diesOutsideRoute ? 1 : 0, deathReleases);
            Assert.Equal(2, spawns);
            if (!diesOutsideRoute) Assert.True(arrived >= 2);
            Assert.Equal(2UL, behavior.Status!.Identity.WorldGeneration);
        }
        finally
        {
            timeout.Cancel();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
