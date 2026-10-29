using Aurelian.Actuation.Host;
using Marionette.Skyrim;

namespace Marionette.Skyrim.App;

public sealed record LiveSaveCorrelationReport(
    int ProtocolVersion,
    string SessionId,
    int ObservationsProcessed,
    int SavesCommitted,
    int LoadsRestored,
    int LoadsFailed,
    string? LastOutcome,
    string[] ActiveLineage,
    string[] InactiveLineage);

public sealed partial class MarionetteTransportClient
{
    public ValueTask<LiveSaveCorrelationReport> RunLiveSaveCorrelationAsync(CancellationToken cancellationToken)
    {
        return RunWorldHostAsync([], cancellationToken);
    }

    public async ValueTask<LiveSaveCorrelationReport> RunWorldHostAsync(
        IReadOnlyList<ISkyrimWorldBehavior> behaviors, CancellationToken cancellationToken,
        TextReader? playtestCommands = null)
    {
        if (string.IsNullOrWhiteSpace(_config.CheckpointDirectory))
        {
            throw new InvalidDataException("checkpoint_directory_required");
        }
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(
            ".", _config.PipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        Console.Error.WriteLine("SKYRIM_HOST waiting_for_bridge");
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        ServerHello hello = await AuthenticateAsync(pipe, cancellationToken).ConfigureAwait(false);
        foreach (string capability in new[] { "query_lifecycle_observations", "query_world_npcs" })
        {
            if (!hello.Capabilities.Contains(capability, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"host_capability_missing:{capability}");
            }
        }
        if (_config.StartupSaveId is not null)
        {
            string requestId = Guid.NewGuid().ToString("N");
            using var loadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            loadTimeout.CancelAfter(TimeSpan.FromSeconds(6));
            await MarionetteWireProtocol.WriteAsync(pipe, new LoadDevelopmentSessionRequest(
                1, "load_development_session", requestId, _config.StartupSaveId, 5000), loadTimeout.Token).ConfigureAwait(false);
            SessionLoadResult load = await MarionetteWireProtocol.ReadAsync<SessionLoadResult>(pipe, loadTimeout.Token).ConfigureAwait(false);
            if (load.RequestId != requestId || load.MessageKind != "load_development_session_result"
                || load.SaveId != _config.StartupSaveId || load.Status != "loading")
            {
                throw new InvalidDataException($"startup_save_rejected:{load.FailureReason}");
            }
            Console.Error.WriteLine($"SKYRIM_HOST startup_save={load.SaveId} generation={load.LoadGeneration}");
        }
        SkyrimSessionId session = CreateSessionId(hello.SessionId!);
        var world = new SkyrimWorldOwnerRuntime(hello.SessionId!);
        world.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
        TickWorldOwner(world);
        var store = new SkyrimCheckpointStore(_config.CheckpointDirectory);
        var coordinator = new SkyrimLiveLifecycleCoordinator(
            session, world, store, new BodyBindingRegistry(), hello.SessionId!);
        Console.Error.WriteLine("SKYRIM_HOST connected world_state=AwaitingWorld");
        ulong afterSequence = 0;
        ulong lastGeneration = 0;
        int processed = 0;
        int saves = 0;
        int restoredLoads = 0;
        int failedLoads = 0;
        string? lastOutcome = null;
        bool firstTick = true;
        bool embedded = hello.Capabilities.Contains("query_world_checkpoint", StringComparer.Ordinal);
        OwnedNpcAgentBehavior? ownedAgent = behaviors.OfType<OwnedNpcAgentBehavior>().SingleOrDefault();
        await using SkyrimHostPlaytestControl? control = _config.EnablePlaytestShell
            ? new SkyrimHostPlaytestControl(pipe, playtestCommands ?? Console.In, cancellationToken, ownedAgent) : null;
        var uptime = System.Diagnostics.Stopwatch.StartNew();
        double lastTickSeconds = 0;
        double nextHeartbeat = 0;
        double readySeconds = 0;
        long readyTicks = 0;
        bool previouslyReady = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (embedded)
                {
                    coordinator.EmbeddedCheckpoint = await ExchangeCheckpointAsync(pipe,
                        new WorldCheckpointRequest(1, "query_world_checkpoint", Guid.NewGuid().ToString("N")), cancellationToken);
                }
                SkyrimStateResult state = await QueryHostStateAsync(pipe, cancellationToken).ConfigureAwait(false);
                // Drain lifecycle events after the engine query: a load may complete
                // while that query waits for Skyrim's main thread.
                LifecycleObservationsResult batch = await QueryHostLifecycleAsync(
                    pipe, afterSequence, cancellationToken).ConfigureAwait(false);
                // Attaching to an already-loaded world starts at its present lifecycle
                // cursor. Historical saves and loads are not replayed into a new owner.
                if (firstTick && state.WorldReady)
                {
                    afterSequence = batch.Observations.LastOrDefault()?.Sequence ?? 0;
                    if (embedded && state.GameTimeDays.HasValue)
                    {
                        coordinator.EmbeddedCheckpoint = await ExchangeCheckpointAsync(pipe,
                            new WorldCheckpointRequest(1, "query_world_checkpoint", Guid.NewGuid().ToString("N")), cancellationToken);
                        var baseline = new SkyrimSaveIdentity("native-loaded-world", new SkyrimTimelineStamp(session,
                            new SkyrimGameTimestamp(state.GameTimeDays.Value), 1), 1);
                        var restored = coordinator.RestoreEmbedded(baseline);
                        ownedAgent?.ObserveLifecycle(new LifecycleObservation("load_completed", 1, 1,
                            baseline.SaveName, state.GameTimeDays, "embedded_attach"), restored, coordinator.CurrentWorld);
                        Console.Error.WriteLine($"SKYRIM_HOST embedded_attach={restored.Outcome}");
                    }
                }
                else
                {
                    foreach (LifecycleObservation observation in batch.Observations)
                    {
                        if (observation.Sequence != afterSequence + 1)
                        {
                            throw new InvalidDataException("lifecycle_history_gap");
                        }
                        if (embedded && observation.Kind == "load_completed")
                        {
                            coordinator.EmbeddedCheckpoint = await ExchangeCheckpointAsync(pipe,
                                new WorldCheckpointRequest(1, "query_world_checkpoint", Guid.NewGuid().ToString("N")), cancellationToken);
                        }
                        if (!embedded && observation.Kind == "save_started" && ownedAgent is not null && !ownedAgent.SavePrepared)
                        {
                            coordinator.CurrentWorld.RequireRestoration("owned_agent_save_requires_preparation");
                        }
                        SkyrimLifecycleProcessingResult result = coordinator.Process(observation);
                        ownedAgent?.ObserveLifecycle(observation, result, coordinator.CurrentWorld);
                        afterSequence = observation.Sequence;
                        processed++;
                        lastOutcome = result.Outcome;
                        if (result.Outcome is "save_checkpoint_committed" or "embedded_save_serialized") saves++;
                        else if (result.Outcome == "checkpoint_restored") restoredLoads++;
                        else if (result.Outcome == "load_failed_without_restore") failedLoads++;
                        Console.Error.WriteLine($"SKYRIM_HOST lifecycle={observation.Kind} sequence={observation.Sequence} " +
                            $"operation={observation.OperationId} outcome={result.Outcome} state={coordinator.CurrentWorld.State}");
                    }
                }
                firstTick = false;
                SkyrimWorldOwnerRuntime current = coordinator.CurrentWorld;
                bool nativeReady = state.WorldReady && !state.WorldPaused && !state.ActiveHostSession;
                if (nativeReady && state.GameTimeDays.HasValue && current.State is
                    SkyrimWorldOwnerState.AwaitingWorld or SkyrimWorldOwnerState.WorldPaused or SkyrimWorldOwnerState.WorldReady)
                {
                    if (current.State != SkyrimWorldOwnerState.WorldReady || lastGeneration != state.WorldGeneration)
                    {
                        current.Post(new SkyrimWorldFact(SkyrimWorldFactKind.WorldReady, current.LastFactSequence + 1,
                            new SkyrimTimelineStamp(session, new SkyrimGameTimestamp(state.GameTimeDays.Value),
                                checked((long)state.RuntimeSequence))));
                        TickWorldOwner(current);
                        Console.Error.WriteLine($"SKYRIM_HOST state=WorldReady generation={state.WorldGeneration}");
                    }
                }
                else if (!nativeReady && current.State == SkyrimWorldOwnerState.WorldReady && current.Timeline is not null)
                {
                    current.Post(new SkyrimWorldFact(SkyrimWorldFactKind.WorldPaused, current.LastFactSequence + 1,
                        current.Timeline));
                    TickWorldOwner(current);
                    Console.Error.WriteLine($"SKYRIM_HOST state=WorldPaused generation={state.WorldGeneration}");
                }
                lastGeneration = state.WorldGeneration;
                bool canRun = SkyrimWorldBehaviorGate.CanRun(coordinator, state);
                double elapsed = uptime.Elapsed.TotalSeconds;
                if (canRun)
                {
                    readyTicks++;
                    if (previouslyReady && elapsed - lastTickSeconds <= 1)
                    {
                        readySeconds += elapsed - lastTickSeconds;
                    }
                }
                previouslyReady = canRun;
                lastTickSeconds = elapsed;
                if (elapsed >= nextHeartbeat)
                {
                    var heartbeat = new HostHeartbeat(elapsed, readySeconds, readyTicks, state.WorldGeneration, canRun);
                    Console.WriteLine("SKYRIM_HOST_HEARTBEAT " + System.Text.Json.JsonSerializer.Serialize(
                        heartbeat, MarionetteWireJsonContext.Default.HostHeartbeat));
                    nextHeartbeat = elapsed + 5;
                }
                try
                {
                    var context = new SkyrimHostContext(pipe, state, current);
                    bool transition = control is not null && await control.TickAsync(context,
                        canRun, readySeconds, readyTicks, cancellationToken).ConfigureAwait(false);
                    if (!transition)
                    {
                        await SkyrimWorldBehaviorGate.TickAsync(coordinator, state,
                            context, behaviors, cancellationToken).ConfigureAwait(false);
                    }
                    if (embedded && canRun && !transition)
                    {
                        string snapshot = store.ExportEmbedded(current);
                        var published = await ExchangeCheckpointAsync(pipe, new WorldCheckpointRequest(1,
                            "publish_world_checkpoint", Guid.NewGuid().ToString("N"),
                            coordinator.EmbeddedCheckpoint!.Epoch, snapshot), cancellationToken);
                        if (published.Status != "completed")
                            Console.Error.WriteLine("SKYRIM_HOST checkpoint_publication_deferred=boundary_changed");
                    }
                }
                catch (SkyrimWorldCommandDeferredException exception)
                {
                    Console.Error.WriteLine($"SKYRIM_HOST behavior_deferred={exception.Message}");
                }
                await Task.Delay(control is null ? 200 : 80, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("SKYRIM_HOST shutdown_requested");
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"SKYRIM_HOST transport_closed={exception.GetType().Name}");
        }
        finally
        {
            coordinator.AbortPendingSaves();
            SkyrimWorldOwnerRuntime current = coordinator.CurrentWorld;
            current.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendDisconnected, current.LastFactSequence + 1));
            TickWorldOwner(current);
        }
        IReadOnlyList<SkyrimCheckpointIndexEntry> entries = store.ReadEntries();
        return new LiveSaveCorrelationReport(1, hello.SessionId!, processed, saves, restoredLoads, failedLoads, lastOutcome,
            entries.Where(entry => entry.ActiveLineage).Select(entry => entry.Save.SaveName).ToArray(),
            entries.Where(entry => !entry.ActiveLineage).Select(entry => entry.Save.SaveName).ToArray());
    }

    private static async ValueTask<LifecycleObservationsResult> QueryHostLifecycleAsync(
        Stream pipe, ulong afterSequence, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        string requestId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe,
            new LifecycleObservationsRequest(1, "query_lifecycle_observations", requestId, afterSequence), timeout.Token).ConfigureAwait(false);
        LifecycleObservationsResult result = await MarionetteWireProtocol.ReadAsync<LifecycleObservationsResult>(pipe, timeout.Token).ConfigureAwait(false);
        if (result.ProtocolVersion != 1 || result.RequestId != requestId || result.MessageKind != "lifecycle_observations_result"
            || result.Status != "completed")
        {
            throw new InvalidDataException("lifecycle_observation_response_invalid");
        }
        return result;
    }

    private static async ValueTask<SkyrimStateResult> QueryHostStateAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        string requestId = Guid.NewGuid().ToString("N");
        await MarionetteWireProtocol.WriteAsync(pipe,
            new SkyrimStateRequest(1, "query_skyrim_state", requestId, 2000), timeout.Token).ConfigureAwait(false);
        SkyrimStateResult result = await MarionetteWireProtocol.ReadAsync<SkyrimStateResult>(pipe, timeout.Token).ConfigureAwait(false);
        if (result.ProtocolVersion != 1 || result.RequestId != requestId || result.MessageKind != "skyrim_state_result")
        {
            throw new InvalidDataException("world_state_response_invalid");
        }
        if (result.Status == "failed" && result.Diagnostic is
            "dispatch_timeout" or "dispatch_unavailable" or "runtime_unavailable" or "queue_full")
        {
            Console.Error.WriteLine($"SKYRIM_HOST world_query_deferred={result.Diagnostic}");
            return result with { WorldReady = false, WorldPaused = true };
        }
        if (result.Status != "completed")
        {
            throw new InvalidDataException($"world_state_failed:{result.Diagnostic}");
        }
        return result;
    }
}
