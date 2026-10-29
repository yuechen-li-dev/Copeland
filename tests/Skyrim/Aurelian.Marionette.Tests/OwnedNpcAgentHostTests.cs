using System.IO.Pipes;
using System.Numerics;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class OwnedNpcAgentHostTests
{
    [Fact]
    public async Task RealHostGatesPauseStopsAfterBodyLossAndRebindsOnlyInANewWorld()
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
        int arrived = 0;
        bool paused = false;
        bool manuallyReleased = false;
        bool sawStopped = false;
        bool active = false;
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
                    if (generation == 2 && moves >= 3) break;
                    await MarionetteWireProtocol.WriteAsync(server, new SkyrimStateResult(1, "skyrim_state_result",
                        request.RequestId, ++sequence, "completed", null, true, sequence, true, 0x14,
                        null, false, null, null, false, null, null, 0x14, GameTimeDays: 10,
                        WorldReady: true, WorldPaused: paused, WorldGeneration: generation), timeout.Token);
                    continue;
                }
                if (request.MessageKind == "query_lifecycle_observations")
                {
                    await MarionetteWireProtocol.WriteAsync(server, new LifecycleObservationsResult(1,
                        "lifecycle_observations_result", request.RequestId, ++sequence, "completed", []), timeout.Token);
                    continue;
                }
                Assert.False(paused);
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
                    target = new Vector3(request.TargetPositionX, request.TargetPositionY, request.TargetPositionZ);
                    // The second request models a native stopping receipt.
                    // Acceptance still needs a later observed arrival.
                    string acceptedState = moves == 2 ? "stopping" : "walking";
                    movement = new OwnedNpcMovement((ulong)moves, acceptedState, "", target.X, target.Y, target.Z,
                        request.StoppingDistance, Vector3.Distance(position, target), 0, 0xFF000900, true);
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
                    if (arrived >= 2 && !manuallyReleased && behavior.Status?.CompletedLegs >= 2)
                    {
                        manuallyReleased = true;
                        active = false;
                        movement = movement! with { MarkerActive = false };
                    }
                }
                bool moving = movement?.State == "walking";
                WorldNpcObservation[] npcs = active ? [new WorldNpcObservation(identity!.FormId,
                    identity.BaseFormId, 0x165A3, "Hunter", position.X, position.Y, position.Z, 100,
                    100, 100, true, false, moving, moving, true, moving ? 80 : 0)] : [];
                string ownedState = identity is null ? "none" : active ? "live" : "released";
                await MarionetteWireProtocol.WriteAsync(server, new WorldNpcResult(1, "world_npc_result",
                    request.RequestId, ++sequence, "completed", "", generation, active ? ++observation : 0, 0, 0,
                    npcs, identity, ownedState, (ulong)spawns, active ? (ulong)spawns - 1 : (ulong)spawns,
                    active ? 1U : 0U, active ? 1U : 0U, true, movement), timeout.Token);
            }
            server.Disconnect();
        }, timeout.Token);
        try
        {
            var client = new MarionetteTransportClient(config);
            await client.RunWorldHostAsync([behavior], timeout.Token);
            await serve;
            Assert.True(sawStopped);
            Assert.Equal(2, spawns);
            Assert.True(arrived >= 2);
            Assert.Equal(2UL, behavior.Status!.Identity.WorldGeneration);
        }
        finally
        {
            timeout.Cancel();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
