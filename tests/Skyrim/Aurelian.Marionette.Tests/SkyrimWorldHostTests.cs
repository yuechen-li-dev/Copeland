using System.IO.Pipes;
using Aurelian.Actuation.Host;
using Marionette.Skyrim;
using Marionette.Skyrim.App;
using Xunit;

namespace Marionette.Skyrim.App.Tests;

public sealed class SkyrimWorldHostTests
{
    [Fact]
    public async Task HostServicesMenuInputOnItsOwnConnectionWhileGameplayIsPaused()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "skyrim-control-" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var config = new LocalTransportConfig("host-control-" + Guid.NewGuid().ToString("N"), "test", "test",
            CheckpointDirectory: directory, EnablePlaytestShell: true);
        using var server = new NamedPipeServerStream(config.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var inputKinds = new List<string>();
        int queries = 0;
        ulong sequence = 0;
        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await MarionetteWireProtocol.ReadAsync<ClientHello>(server, timeout.Token);
            await MarionetteWireProtocol.WriteAsync(server, new ServerHello(1, "server_hello", true,
                "control-session", "test", ["query_lifecycle_observations", "query_world_npcs", "playtest_begin"], null), timeout.Token);
            while (true)
            {
                PlaytestRequest request = await MarionetteWireProtocol.ReadAsync<PlaytestRequest>(server, timeout.Token);
                if (request.MessageKind == "query_skyrim_state")
                {
                    if (++queries > 5) break;
                    await MarionetteWireProtocol.WriteAsync(server,
                        State(request.RequestId, ++sequence) with { WorldPaused = true }, timeout.Token);
                }
                else if (request.MessageKind == "query_lifecycle_observations")
                {
                    await MarionetteWireProtocol.WriteAsync(server, new LifecycleObservationsResult(
                        1, "lifecycle_observations_result", request.RequestId, ++sequence, "completed", []), timeout.Token);
                }
                else if (request.MessageKind.StartsWith("playtest_", StringComparison.Ordinal)
                    || request.MessageKind == "query_playtest_state")
                {
                    inputKinds.Add(request.MessageKind);
                    Assert.DoesNotContain((uint)PlaytestButtons.Forward, new[] { request.InputButtons });
                    await MarionetteWireProtocol.WriteAsync(server, new PlaytestObservation(1, "playtest_result",
                        request.RequestId, ++sequence, "completed", "", true, false, false, true,
                        request.InputButtons, 1, 0x14, 0, 0, 0, 0, 0), timeout.Token);
                }
                else throw new InvalidDataException("unexpected request:" + request.MessageKind);
            }
            server.Disconnect();
        });
        var behavior = new CountingBehavior();
        try
        {
            using var commands = new StringReader("input MenuAccept 0.1\ninput Forward 1\ninspect\n");
            await new MarionetteTransportClient(config).RunWorldHostAsync([behavior], timeout.Token, commands);
            await serve;
            Assert.Contains("playtest_begin", inputKinds);
            Assert.Contains("playtest_update", inputKinds);
            Assert.Contains("playtest_end", inputKinds);
            Assert.Empty(behavior.Generations);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OrdinaryNpcObservationUsesGeneratedWireMetadata()
    {
        var npc = new WorldNpcObservation(0x123, 0x456, 0x789, "Ordinary NPC",
            1, 2, 3, 20, 100, 100, Loaded: true, Dead: false);
        var expected = new WorldNpcResult(1, "world_npc_result", "observe", 10,
            "completed", "", 3, 4, 0, 0, [npc]);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(expected));
        WorldNpcResult actual = await MarionetteWireProtocol.ReadAsync<WorldNpcResult>(stream, default);
        Assert.Equal(npc, Assert.Single(actual.Npcs));
        Assert.Equal(3UL, actual.WorldGeneration);
        Assert.Equal(4UL, actual.ObservationSequence);
    }

    [Fact]
    public async Task OrdinaryHostKeepsRunningWithoutCorpsesAndGatesPauseAndLoad()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "skyrim-host-" + Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var config = new LocalTransportConfig("world-host-" + Guid.NewGuid().ToString("N"), "test", "test",
            CheckpointDirectory: directory);
        using var server = new NamedPipeServerStream(config.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var kinds = new List<string>();
        int stateQueries = 0;
        ulong sequence = 0;
        Task serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await MarionetteWireProtocol.ReadAsync<ClientHello>(server, timeout.Token);
            await MarionetteWireProtocol.WriteAsync(server, new ServerHello(1, "server_hello", true,
                "ordinary-host-session", "test", ["query_lifecycle_observations", "query_world_npcs"], null), timeout.Token);
            while (true)
            {
                WorldNpcRequest request = await MarionetteWireProtocol.ReadAsync<WorldNpcRequest>(server, timeout.Token);
                kinds.Add(request.MessageKind);
                if (request.MessageKind == "query_lifecycle_observations")
                {
                    LifecycleObservation[] observations = stateQueries switch
                    {
                        4 => [new("load_started", 1, 1, "OrdinaryWorld", 10, "SKSE::kPreLoadGame")],
                        5 => [new("load_completed", 2, 1, "OrdinaryWorld", 20, "SKSE::kPostLoadGame")],
                        _ => [],
                    };
                    await MarionetteWireProtocol.WriteAsync(server, new LifecycleObservationsResult(
                        1, "lifecycle_observations_result", request.RequestId, ++sequence, "completed", observations), timeout.Token);
                }
                else if (request.MessageKind == "query_skyrim_state")
                {
                    if (stateQueries == 5) break;
                    stateQueries++;
                    SkyrimStateResult state = State(request.RequestId, ++sequence) with
                    {
                        WorldReady = stateQueries is 2 or 3 or 5,
                        WorldPaused = stateQueries == 3,
                        WorldGeneration = stateQueries >= 4 ? 2UL : 1UL,
                        Status = stateQueries == 4 ? "failed" : "completed",
                        Diagnostic = stateQueries == 4 ? "dispatch_timeout" : null,
                    };
                    await MarionetteWireProtocol.WriteAsync(server, state, timeout.Token);
                }
                else if (request.MessageKind == "query_world_npcs")
                {
                    // No NPC or corpse exists in this world. Readiness and subsequent ticks still succeed.
                    await MarionetteWireProtocol.WriteAsync(server, new WorldNpcResult(1, "world_npc_result",
                        request.RequestId, ++sequence, "completed", "", stateQueries >= 4 ? 2UL : 1UL,
                        sequence, 0, 0, []), timeout.Token);
                }
                else throw new InvalidDataException("unexpected_request:" + request.MessageKind);
            }
            server.Disconnect();
        });
        var behavior = new CountingBehavior();
        try
        {
            LiveSaveCorrelationReport report = await new MarionetteTransportClient(config)
                .RunWorldHostAsync([behavior], timeout.Token);
            await serve;
            Assert.Equal(new ulong[] { 1, 2 }, behavior.Generations);
            Assert.Equal(2, report.ObservationsProcessed);
            Assert.Equal("load_completed_without_checkpoint", report.LastOutcome);
            Assert.DoesNotContain("query_eligible_host_fixtures", kinds);
            Assert.Equal(5, stateQueries);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BehaviorGateBlocksSavePauseLoadPossessionAndRestoration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "skyrim-gate-" + Guid.NewGuid().ToString("N"));
        var session = new SkyrimSessionId(Guid.NewGuid());
        var timeline = new SkyrimTimelineStamp(session, new SkyrimGameTimestamp(10), 1);
        var world = new SkyrimWorldOwnerRuntime("gate-test");
        world.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
        Tick(world);
        world.Post(new SkyrimWorldFact(SkyrimWorldFactKind.WorldReady, 2, timeline));
        Tick(world);
        var coordinator = new SkyrimLiveLifecycleCoordinator(session, world,
            new SkyrimCheckpointStore(directory), new BodyBindingRegistry(), "gate-test");
        SkyrimStateResult state = State("ready", 1);
        try
        {
            Assert.True(SkyrimWorldBehaviorGate.CanRun(coordinator, state));
            Assert.False(SkyrimWorldBehaviorGate.CanRun(coordinator, state with { WorldPaused = true }));
            Assert.False(SkyrimWorldBehaviorGate.CanRun(coordinator, state with { WorldReady = false }));
            Assert.False(SkyrimWorldBehaviorGate.CanRun(coordinator, state with { ActiveHostSession = true }));
            coordinator.Process(new LifecycleObservation("save_started", 1, 1, "GateSave", 10, "SKSE::kSaveGame"));
            Assert.False(SkyrimWorldBehaviorGate.CanRun(coordinator, state));
            coordinator.Process(new LifecycleObservation("save_serialized", 2, 1, "GateSave", 10, "SaveCallback"));
            Assert.True(SkyrimWorldBehaviorGate.CanRun(coordinator, state));
            coordinator.Process(new LifecycleObservation("load_started", 3, 2, "OtherSave", 11, "SKSE::kPreLoadGame"));
            Assert.False(SkyrimWorldBehaviorGate.CanRun(coordinator, state));
            world.Post(new SkyrimWorldFact(SkyrimWorldFactKind.RestorationRequired, 100,
                Reason: "binding_release_unconfirmed"));
            Tick(world);
            Assert.False(SkyrimWorldBehaviorGate.CanRun(coordinator, state));
        }
        finally
        {
            coordinator.AbortPendingSaves();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static SkyrimStateResult State(string requestId, ulong sequence)
    {
        return new SkyrimStateResult(1, "skyrim_state_result", requestId, sequence, "completed", null,
            true, sequence, true, 0x14, null, false, null, null, false, null, null, 0x14,
            GameTimeDays: 10, WorldReady: true, WorldPaused: false, WorldGeneration: 1);
    }

    private static void Tick(SkyrimWorldOwnerRuntime world)
    {
        for (int index = 0; index < 6; index++) world.Tick();
    }

    private sealed class CountingBehavior : ISkyrimWorldBehavior
    {
        public List<ulong> Generations { get; } = [];

        public async ValueTask TickAsync(SkyrimHostContext context, CancellationToken cancellationToken)
        {
            WorldNpcResult result = await context.ObserveNpcsAsync(cancellationToken);
            Assert.Empty(result.Npcs);
            Generations.Add(context.State.WorldGeneration);
        }
    }
}
