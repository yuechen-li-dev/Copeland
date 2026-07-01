namespace Aurelian.Core.Engine;

public sealed class AurelianEngine
{
    public AurelianEngine(AurelianEngineOptions? options = null)
    {
        Options = options ?? new AurelianEngineOptions();
        Status = AurelianEngineStatus.Created;
    }

    public AurelianEngineOptions Options { get; }

    public AurelianEngineStatus Status { get; private set; }

    public AurelianEngineResult Start()
    {
        if (Status == AurelianEngineStatus.Started)
        {
            return AurelianEngineResult.Failed(
                Status,
                Diagnostic(
                    AurelianEngineDiagnosticCodes.EngineAlreadyStarted,
                    "Aurelian engine is already started."));
        }

        Status = AurelianEngineStatus.Started;
        return AurelianEngineResult.Successful(Status);
    }

    public AurelianEngineResult Stop()
    {
        if (Status != AurelianEngineStatus.Started)
        {
            return AurelianEngineResult.Failed(
                Status,
                Diagnostic(
                    AurelianEngineDiagnosticCodes.EngineAlreadyStopped,
                    "Aurelian engine is not started."));
        }

        Status = AurelianEngineStatus.Stopped;
        return AurelianEngineResult.Successful(Status);
    }

    private static AurelianEngineDiagnostic Diagnostic(string code, string message) =>
        new(code, AurelianEngineDiagnosticSeverity.Error, message);
}
