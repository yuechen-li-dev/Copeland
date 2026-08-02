using System.Text.Json;

namespace Aurelian.Marionette.Transport;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || (args[0] != "loopback" && args[0] != "scenario" && args[0] != "session-bootstrap") || args[1] != "--config")
        {
            Console.Error.WriteLine("Usage: Aurelian.Marionette.Transport <loopback|scenario|session-bootstrap> --config <ignored-local-config.json>");
            return 2;
        }

        try
        {
            LocalTransportConfig config = LocalTransportConfig.Load(args[2]);
            var client = new MarionetteTransportClient(config);
            if (args[0] == "scenario") {
                KnownActuatorReport report = await client.RunKnownActuatorScenarioAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.KnownActuatorReport));
            } else if (args[0] == "session-bootstrap") {
                SessionBootstrapReport report = await client.RunSessionBootstrapAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.SessionBootstrapReport));
            } else {
                LoopbackReport report = await client.RunLoopbackAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(report, MarionetteWireJsonContext.Default.LoopbackReport));
                Console.WriteLine($"ED-M2b.2c query succeeded: player={(report.PlayerFormId.HasValue ? $"0x{report.PlayerFormId:X8}" : "unavailable")}, pending={report.PendingRequestPresent}, host={report.ActiveHostSession}.");
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException or OperationCanceledException)
        {
            Console.Error.WriteLine($"ED-M2b.2b loopback failed: {exception.Message}");
            return 1;
        }
    }
}
