using Aurelian.Actuation.Host;

namespace Aurelian.Marionette.Transport;

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
    public async ValueTask<LiveSaveCorrelationReport> RunLiveSaveCorrelationAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.CheckpointDirectory))
        {
            throw new InvalidDataException("checkpoint_directory_required");
        }

        using var pipe = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            _config.PipeName,
            System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        ServerHello hello = await AuthenticateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!hello.Capabilities.Contains("query_lifecycle_observations", StringComparer.Ordinal))
        {
            throw new InvalidDataException("lifecycle_observation_capability_missing");
        }

        SkyrimStateResult initial = await QueryStateAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!initial.GameTimeDays.HasValue)
        {
            throw new InvalidDataException("skyrim_game_timestamp_unavailable");
        }
        SkyrimSessionId session = CreateSessionId(hello.SessionId!);
        var world = new SkyrimWorldOwnerRuntime(hello.SessionId!);
        world.Post(new SkyrimWorldFact(SkyrimWorldFactKind.BackendConnected, 1));
        TickWorldOwner(world);
        world.Post(new SkyrimWorldFact(
            SkyrimWorldFactKind.WorldReady,
            2,
            new SkyrimTimelineStamp(
                session,
                new SkyrimGameTimestamp(initial.GameTimeDays.Value),
                checked((long)initial.RuntimeSequence))));
        TickWorldOwner(world);
        await RediscoverBodiesAsync(pipe, world, hello.SessionId!, cancellationToken).ConfigureAwait(false);

        var store = new SkyrimCheckpointStore(_config.CheckpointDirectory);
        var coordinator = new SkyrimLiveLifecycleCoordinator(
            session,
            world,
            store,
            new BodyBindingRegistry(),
            hello.SessionId!);
        Console.Error.WriteLine("M4A_MANAGED world_state=WorldReady lifecycle=polling");

        ulong afterSequence = 0;
        int processed = 0;
        int saves = 0;
        int restoredLoads = 0;
        int failedLoads = 0;
        string? lastOutcome = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string requestId = Guid.NewGuid().ToString("N");
                await MarionetteWireProtocol.WriteAsync(
                    pipe,
                    new LifecycleObservationsRequest(
                        MarionetteWireProtocol.Version,
                        "query_lifecycle_observations",
                        requestId,
                        afterSequence),
                    cancellationToken).ConfigureAwait(false);
                LifecycleObservationsResult batch = await MarionetteWireProtocol.ReadAsync<LifecycleObservationsResult>(
                    pipe,
                    cancellationToken).ConfigureAwait(false);
                if (batch.MessageKind != "lifecycle_observations_result"
                    || batch.RequestId != requestId
                    || batch.Status != "completed")
                {
                    throw new InvalidDataException("lifecycle_observation_response_invalid");
                }

                foreach (LifecycleObservation observation in batch.Observations)
                {
                    SkyrimLifecycleProcessingResult result = coordinator.Process(observation);
                    afterSequence = Math.Max(afterSequence, observation.Sequence);
                    processed++;
                    lastOutcome = result.Outcome;
                    if (result.Outcome == "save_checkpoint_committed")
                    {
                        saves++;
                    }
                    else if (result.Outcome == "checkpoint_restored")
                    {
                        restoredLoads++;
                        await RediscoverBodiesAsync(
                            pipe,
                            coordinator.CurrentWorld,
                            hello.SessionId!,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else if (result.Outcome == "load_failed_without_restore")
                    {
                        failedLoads++;
                    }
                    Console.Error.WriteLine(
                        $"M4A_MANAGED kind={observation.Kind} operation={observation.OperationId} " +
                        $"sequence={observation.Sequence} save={observation.SaveName ?? "none"} " +
                        $"outcome={result.Outcome} world_state={coordinator.CurrentWorld.State}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException
            && !cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"M4A_MANAGED transport_closed={exception.GetType().Name}");
        }
        finally
        {
            coordinator.AbortPendingSaves();
        }

        IReadOnlyList<SkyrimCheckpointIndexEntry> entries = store.ReadEntries();
        return new LiveSaveCorrelationReport(
            MarionetteWireProtocol.Version,
            hello.SessionId!,
            processed,
            saves,
            restoredLoads,
            failedLoads,
            lastOutcome,
            entries.Where(entry => entry.ActiveLineage)
                .Select(entry => entry.Save.SaveName)
                .ToArray(),
            entries.Where(entry => !entry.ActiveLineage)
                .Select(entry => entry.Save.SaveName)
                .ToArray());
    }

    private async ValueTask RediscoverBodiesAsync(
        Stream pipe,
        SkyrimWorldOwnerRuntime world,
        string sessionScope,
        CancellationToken cancellationToken)
    {
        StableHostCandidateQuery query = await QueryStableHostCandidatesAsync(
            pipe,
            cancellationToken).ConfigureAwait(false);
        SkyrimCandidateSet candidates = SkyrimCandidateLowerer.Lower(
            sessionScope,
            query.First,
            world.Registry);
        long sequence = world.LastFactSequence;
        foreach (AgentBodyCandidate candidate in candidates.Candidates)
        {
            SkyrimBodyCandidateMapping mapping = candidates.BackendMappings[candidate.Body.Id];
            world.Post(new SkyrimWorldFact(
                SkyrimWorldFactKind.BodyLoaded,
                ++sequence,
                Body: candidate.Body,
                Origin: mapping.Origin,
                ImportedData: candidate.Agent.Data));
            TickWorldOwner(world);
        }
    }
}
