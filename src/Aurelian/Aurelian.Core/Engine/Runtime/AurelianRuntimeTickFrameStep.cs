using Aurelian.Core.Engine.Frames;
using Aurelian.Runtime.Sessions;

namespace Aurelian.Core.Engine.Runtime;

public sealed class AurelianRuntimeTickFrameStep
{
    private readonly IAurelianRuntimeTicker? runtimeTicker;

    public AurelianRuntimeTickFrameStep(IAurelianRuntimeTicker runtimeTicker)
    {
        this.runtimeTicker = runtimeTicker;
    }

    public async Task<AurelianRuntimeTickFrameStepResult> RunAsync(
        AurelianFrameId frameId,
        TimeSpan deltaTime,
        CancellationToken cancellationToken = default)
    {
        if (runtimeTicker is null)
        {
            return Result(
                AurelianRuntimeTickFrameStepStatus.Rejected,
                frameId,
                null,
                Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickerMissing, "Aurelian runtime tick frame step requires a runtime ticker."));
        }

        if (deltaTime < TimeSpan.Zero)
        {
            return Result(
                AurelianRuntimeTickFrameStepStatus.Rejected,
                frameId,
                null,
                Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.InvalidDeltaTime, "Aurelian runtime tick frame step delta time must be non-negative."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                AurelianRuntimeTickFrameStepStatus.Cancelled,
                frameId,
                null,
                Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickCancelled, "Aurelian runtime tick frame step was cancelled."));
        }

        AurelianRuntimeTickInput input = new(frameId.Value, deltaTime);

        try
        {
            AurelianRuntimeTickResult runtimeResult = await runtimeTicker
                .TickAsync(input, cancellationToken)
                .ConfigureAwait(false);

            return runtimeResult.Status switch
            {
                AurelianRuntimeTickStatus.Ticked when runtimeResult.Success => new AurelianRuntimeTickFrameStepResult(AurelianRuntimeTickFrameStepStatus.Ticked, frameId, runtimeResult, []),
                AurelianRuntimeTickStatus.Rejected => Result(AurelianRuntimeTickFrameStepStatus.Rejected, frameId, runtimeResult, Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickRejected, "Aurelian runtime tick was rejected.")),
                AurelianRuntimeTickStatus.Cancelled => Result(AurelianRuntimeTickFrameStepStatus.Cancelled, frameId, runtimeResult, Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickCancelled, "Aurelian runtime tick was cancelled.")),
                _ => Result(AurelianRuntimeTickFrameStepStatus.Failed, frameId, runtimeResult, Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickFailed, "Aurelian runtime tick failed.")),
            };
        }
        catch (OperationCanceledException)
        {
            return Result(
                AurelianRuntimeTickFrameStepStatus.Cancelled,
                frameId,
                null,
                Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickCancelled, "Aurelian runtime tick frame step was cancelled."));
        }
        catch (Exception ex)
        {
            return Result(
                AurelianRuntimeTickFrameStepStatus.Failed,
                frameId,
                null,
                Diagnostic(AurelianRuntimeTickFrameStepDiagnosticCodes.RuntimeTickFailed, $"Aurelian runtime ticker failed: {ex.Message}"));
        }
    }

    private static AurelianRuntimeTickFrameStepResult Result(
        AurelianRuntimeTickFrameStepStatus status,
        AurelianFrameId frameId,
        AurelianRuntimeTickResult? runtimeResult,
        AurelianRuntimeTickFrameStepDiagnostic diagnostic) =>
        new(status, frameId, runtimeResult, [diagnostic]);

    private static AurelianRuntimeTickFrameStepDiagnostic Diagnostic(string code, string message) =>
        new(code, AurelianRuntimeTickFrameStepDiagnosticSeverity.Error, message);
}
