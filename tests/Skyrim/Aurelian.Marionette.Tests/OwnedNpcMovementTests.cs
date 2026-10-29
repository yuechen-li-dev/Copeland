using System.IO.Pipes;
using System.Numerics;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class OwnedNpcMovementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostRequiresObservedWalkingAndCorrelatesCancellation(bool teleportArrival)
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "owned-walking-" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var config = new LocalTransportConfig("walking-" + Guid.NewGuid().ToString("N"), "test", "test",
            CheckpointDirectory: directory);
        using var server = new NamedPipeServerStream(config.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var proof = new OwnedNpcMovementProofBehavior(TimeSpan.Zero);
        var identity = new OwnedNpcIdentity(1, 7, 0xFF000800, 0x73FBF);
        Vector3 position = new(0, 0, 0);
        Vector3 goal = position;
        OwnedNpcMovement? movement = null;
        bool spawned = false;
        bool active = false;
        ulong sequence = 0;
        ulong observation = 0;
        ulong movementId = 0;
        int polls = 0;
        int rejectedCancels = 0;
        uint duration = 0;
        string? pendingTerminal = null;
        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await MarionetteWireProtocol.ReadAsync<ClientHello>(server, timeout.Token);
            await MarionetteWireProtocol.WriteAsync(server, new ServerHello(1, "server_hello", true,
                "walking-session", "test", ["query_world_npcs", "query_lifecycle_observations"], null), timeout.Token);
            while (true)
            {
                WorldNpcRequest request;
                try { request = await MarionetteWireProtocol.ReadAsync<WorldNpcRequest>(server, timeout.Token); }
                catch (EndOfStreamException) { break; }
                if (request.MessageKind == "query_skyrim_state")
                {
                    if (proof.CompletedProof is not null) break;
                    await MarionetteWireProtocol.WriteAsync(server, new SkyrimStateResult(1, "skyrim_state_result",
                        request.RequestId, ++sequence, "completed", null, true, sequence, true, 0x14,
                        null, false, null, null, false, null, null, 0x14, GameTimeDays: 10,
                        WorldReady: true, WorldPaused: false, WorldGeneration: 1), timeout.Token);
                    continue;
                }
                if (request.MessageKind == "query_lifecycle_observations")
                {
                    await MarionetteWireProtocol.WriteAsync(server, new LifecycleObservationsResult(1,
                        "lifecycle_observations_result", request.RequestId, ++sequence, "completed", []), timeout.Token);
                    continue;
                }
                string failure = "";
                if (request.MessageKind == "spawn_owned_npc") { spawned = active = true; }
                else if (request.MessageKind == "move_owned_npc")
                {
                    Assert.Equal(identity.LeaseId, request.ExpectedOwnedLeaseId);
                    Assert.Equal(identity.FormId, request.TargetFormId);
                    Assert.Equal(observation, request.ExpectedObservationSequence);
                    goal = new Vector3(request.TargetPositionX, request.TargetPositionY, request.TargetPositionZ);
                    movement = new OwnedNpcMovement(++movementId, "walking", "", goal.X, goal.Y, goal.Z,
                        request.StoppingDistance, Vector3.Distance(position, goal), 0, 0xFF000900, true);
                    duration = request.MovementDurationMilliseconds;
                    polls = 0;
                }
                else if (request.MessageKind == "cancel_owned_npc_movement")
                {
                    if (request.ExpectedMovementId != movement!.MovementId)
                    {
                        rejectedCancels++;
                        failure = "owned_movement_identity_mismatch";
                    }
                    else
                    {
                        pendingTerminal = "cancelled";
                        movement = movement with { State = "stopping" };
                    }
                }
                else if (request.MessageKind == "release_owned_npc")
                {
                    active = false;
                    movement = movement! with { MarkerActive = false };
                }
                else Assert.Equal("query_owned_npc", request.MessageKind);
                if (request.MessageKind == "query_owned_npc" && movement?.State == "stopping")
                {
                    movement = movement with { State = pendingTerminal! };
                    pendingTerminal = null;
                }
                else if (request.MessageKind == "query_owned_npc" && movement?.State == "walking")
                {
                    polls++;
                    if (goal.Z - position.Z > 256)
                    {
                        if (polls >= 3) movement = movement with { State = "blocked" };
                    }
                    if (movement.State is "arrived" or "blocked" or "timed_out")
                    {
                        pendingTerminal = movement.State;
                        movement = movement with { State = "stopping" };
                    }
                    else if (duration == 500 && polls >= 3)
                    {
                        movement = movement with { State = "timed_out", ElapsedSeconds = 0.6f, StopRequestedSeconds = 0.6f };
                    }
                    else
                    {
                        float distance = Vector3.Distance(position, goal);
                        float step = Math.Min(distance, teleportArrival ? 10000 : 40);
                        if (distance > 0) position += Vector3.Normalize(goal - position) * step;
                        distance = Vector3.Distance(position, goal);
                        movement = movement with { Distance = distance, ElapsedSeconds = polls * 0.2f };
                        if (distance <= movement.StoppingDistance) movement = movement with { State = "arrived" };
                    }
                }
                bool moving = movement?.State == "walking";
                WorldNpcObservation[] actors = active ? [new WorldNpcObservation(identity.FormId, identity.BaseFormId,
                    0x165A3, "Hunter", position.X, position.Y, position.Z, 100, 100, 100, true, false,
                    moving, moving, true, moving ? 80 : 0)] : [];
                string state = "none";
                if (spawned) state = active ? "live" : "released";
                await MarionetteWireProtocol.WriteAsync(server, new WorldNpcResult(1, "world_npc_result",
                    request.RequestId, ++sequence, failure.Length == 0 ? "completed" : "failed", failure, 1,
                    ++observation, 0, 0, actors, spawned ? identity : null, state, spawned ? 1UL : 0UL,
                    spawned && !active ? 1UL : 0UL, active ? 1U : 0U, active ? 1U : 0U, true, movement), timeout.Token);
            }
            server.Disconnect();
        });
        try
        {
            var client = new MarionetteTransportClient(config);
            if (teleportArrival)
            {
                InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await client.RunWorldHostAsync([proof], timeout.Token));
                Assert.Equal("walking_not_demonstrated", failure.Message);
                Assert.Null(proof.CompletedProof);
            }
            else
            {
                await client.RunWorldHostAsync([proof], timeout.Token);
                Assert.NotNull(proof.CompletedProof);
                Assert.Equal(["arrived", "cancelled", "blocked", "timed_out"], proof.CompletedProof.TerminalStates);
                Assert.True(proof.CompletedProof.MovingSamples >= 2);
                Assert.True(proof.CompletedProof.MarkerReleased);
                Assert.Equal(1, rejectedCancels);
            }
            await serve;
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
