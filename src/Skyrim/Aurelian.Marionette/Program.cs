using System.Text.Json;

namespace Marionette.Skyrim.App;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || (args[0] != "world-host" && args[0] != "loopback" && args[0] != "scenario" && args[0] != "session-bootstrap" && args[0] != "eligible-host-fixtures" && args[0] != "disconnect-restoration" && args[0] != "dominatus-skyrim" && args[0] != "live-save-correlation") || args[1] != "--config")
        {
            Console.Error.WriteLine("Usage: Aurelian.Marionette <world-host|loopback|scenario|session-bootstrap|eligible-host-fixtures|disconnect-restoration|dominatus-skyrim|live-save-correlation> --config <ignored-local-config.json>");
            return 2;
        }

        try
        {
            LocalTransportConfig config = LocalTransportConfig.Load(args[2]);
            var client = new MarionetteTransportClient(config);
            if (args[0] == "world-host")
            {
                using var shutdown = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, signal) =>
                {
                    signal.Cancel = true;
                    shutdown.Cancel();
                };
                Console.CancelKeyPress += cancel;
                try
                {
                    var behaviors = new List<ISkyrimWorldBehavior> { new ObserveWorldNpcsBehavior() };
                    if (config.EnableNpcNumberProof)
                    {
                        behaviors.Add(new NpcNumberProofBehavior());
                    }
                    LiveSaveCorrelationReport report = await client.RunWorldHostAsync(behaviors, shutdown.Token).ConfigureAwait(false);
                    Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.LiveSaveCorrelationReport));
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
            } else if (args[0] == "scenario") {
                KnownActuatorReport report = await client.RunKnownActuatorScenarioAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.KnownActuatorReport));
            } else if (args[0] == "session-bootstrap") {
                SessionBootstrapReport report = await client.RunSessionBootstrapAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.SessionBootstrapReport));
            } else if (args[0] == "eligible-host-fixtures") {
                DeterministicHostFixtureReport report = await client.RunDeterministicHostFixtureScenarioAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.DeterministicHostFixtureReport));
            } else if (args[0] == "disconnect-restoration") {
                DisconnectRestorationReport report = await client.RunDisconnectRestorationScenarioAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.DisconnectRestorationReport));
            } else if (args[0] == "dominatus-skyrim") {
                DominatusSkyrimReport report = await client.RunDominatusSkyrimScenarioAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.DominatusSkyrimReport));
            } else if (args[0] == "live-save-correlation") {
                LiveSaveCorrelationReport report = await client.RunLiveSaveCorrelationAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.LiveSaveCorrelationReport));
            } else {
                LoopbackReport report = await client.RunLoopbackAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.LoopbackReport));
                Console.WriteLine($"ED-M2b.2c query succeeded: player={(report.PlayerFormId.HasValue ? $"0x{report.PlayerFormId:X8}" : "unavailable")}, pending={report.PendingRequestPresent}, host={report.ActiveHostSession}.");
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException or OperationCanceledException)
        {
            Console.Error.WriteLine($"Marionette controller failed: {exception.Message}");
            return 1;
        }
    }
}
