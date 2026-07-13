using Aurelian.Core.Engine.Commands;

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

    /// <summary>
    /// Indicates that the backend accepted an explicit close command. Hosts use
    /// this fact to decide when their native resources may be disposed.
    /// </summary>
    public bool CloseRequestAccepted { get; private set; }

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

    /// <summary>
    /// Accepts the backend-owned close command at the engine lifecycle
    /// boundary. Acceptance stops a running engine and is intentionally
    /// idempotent so repeated platform close notifications have one result.
    /// </summary>
    public AurelianEngineResult AcceptCloseRequest(AurelianCloseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CloseRequestAccepted && Status == AurelianEngineStatus.Stopped)
        {
            return AurelianEngineResult.Successful(Status);
        }

        if (Status != AurelianEngineStatus.Started)
        {
            return AurelianEngineResult.Failed(
                Status,
                Diagnostic(
                    AurelianEngineDiagnosticCodes.CloseRequestRejected,
                    "Aurelian engine can accept a close request only while started."));
        }

        CloseRequestAccepted = true;
        Status = AurelianEngineStatus.Stopped;
        return AurelianEngineResult.Successful(Status);
    }

    private static AurelianEngineDiagnostic Diagnostic(string code, string message) =>
        new(code, AurelianEngineDiagnosticSeverity.Error, message);
}
