using System.IO.Pipes;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class OwnedNpcProofTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task RealHostProofRequiresNativeOwnershipRefusalsAndBalancedRetirement(
        bool acceptStaleRelease, int expectedCycles)
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "owned-npc-" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var config = new LocalTransportConfig("owned-" + Guid.NewGuid().ToString("N"), "test", "test",
            CheckpointDirectory: directory);
        using var server = new NamedPipeServerStream(config.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var behavior = new OwnedNpcProofBehavior();
        ulong sequence = 0;
        ulong created = 0;
        ulong retired = 0;
        OwnedNpcIdentity? identity = null;
        bool active = false;
        int retirementPolls = 0;
        float speed = 100;
        int rejectedBystanders = 0;
        int rejectedDuplicates = 0;
        bool staleAttempted = false;
        var bystander = new WorldNpcObservation(0xA2C94, 0xA2C8E, 0x165A3, "Lydia",
            0, 0, 0, 100, 100, 100, true, false);
        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await MarionetteWireProtocol.ReadAsync<ClientHello>(server, timeout.Token);
            await MarionetteWireProtocol.WriteAsync(server, new ServerHello(1, "server_hello", true,
                "owned-session", "test", ["query_world_npcs", "query_lifecycle_observations"], null), timeout.Token);
            while (true)
            {
                WorldNpcRequest request;
                try
                {
                    request = await MarionetteWireProtocol.ReadAsync<WorldNpcRequest>(server, timeout.Token);
                }
                catch (EndOfStreamException) { break; }
                if (request.MessageKind == "query_skyrim_state")
                {
                    if (behavior.CompletedCycles.Count == 2) break;
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
                if (request.MessageKind == "spawn_owned_npc")
                {
                    Assert.Equal("hunter", request.NpcBaseKey);
                    if (active)
                    {
                        failure = "owned_npc_already_exists";
                        rejectedDuplicates++;
                    }
                    else
                    {
                        identity = new OwnedNpcIdentity(1, ++created, 0xFF000800 + (uint)created, 0x73FBF);
                        active = true;
                        speed = 100;
                    }
                }
                else if (request.MessageKind == "release_owned_npc")
                {
                    if (request.TargetFormId == bystander.FormId)
                    {
                        failure = "owned_npc_identity_mismatch";
                        rejectedBystanders++;
                    }
                    else if (request.ExpectedOwnedLeaseId != identity!.LeaseId)
                    {
                        staleAttempted = true;
                        if (!acceptStaleRelease) failure = "owned_npc_identity_mismatch";
                    }
                    else if (active && retirementPolls == 0)
                    {
                        retirementPolls = 2;
                    }
                }
                else if (request.MessageKind == "set_npc_base_speed")
                {
                    Assert.True(active);
                    Assert.Equal(identity!.FormId, request.TargetFormId);
                    Assert.Equal(identity.LeaseId, request.ExpectedOwnedLeaseId);
                    Assert.Equal(speed, request.ExpectedBaseSpeed);
                    speed = request.DesiredBaseSpeed;
                }
                else Assert.Contains(request.MessageKind, new[] { "query_owned_npc", "query_world_npcs" });
                if (request.MessageKind == "query_owned_npc" && retirementPolls > 0 && --retirementPolls == 0)
                {
                    active = false;
                    retired++;
                }
                WorldNpcObservation[] actors = [];
                string state = "none";
                if (identity is not null) state = "released";
                if (active)
                {
                    state = "live";
                    actors = [bystander with { FormId = identity!.FormId, BaseFormId = identity.BaseFormId,
                        BaseSpeedMultiplier = speed, SpeedMultiplier = speed }];
                }
                if (request.MessageKind == "query_world_npcs") actors = [bystander];
                if (retirementPolls > 0)
                {
                    state = "retiring";
                    actors = [];
                }
                await MarionetteWireProtocol.WriteAsync(server, new WorldNpcResult(1, "world_npc_result",
                    request.RequestId, ++sequence, failure.Length == 0 ? "completed" : "failed", failure,
                    1, sequence, 100, speed, actors, identity, state,
                    created, retired, active ? 1U : 0U, active && retirementPolls == 0 ? 1U : 0U, true), timeout.Token);
            }
            server.Disconnect();
        });
        try
        {
            var client = new MarionetteTransportClient(config);
            if (acceptStaleRelease)
            {
                InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                    await client.RunWorldHostAsync([behavior], timeout.Token));
                Assert.Equal("expected_native_refusal_missing:owned_npc_identity_mismatch", failure.Message);
            }
            else
            {
                await client.RunWorldHostAsync([behavior], timeout.Token);
            }
            await serve;
            Assert.Equal(expectedCycles, behavior.CompletedCycles.Count);
            Assert.Equal(2, rejectedBystanders);
            Assert.Equal(2, rejectedDuplicates);
            Assert.True(staleAttempted);
            if (!acceptStaleRelease)
            {
                Assert.False(active);
                Assert.Equal(created, retired);
                Assert.All(behavior.CompletedCycles, proof => Assert.Equal(proof.BaselineCount, proof.FinalCount));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
