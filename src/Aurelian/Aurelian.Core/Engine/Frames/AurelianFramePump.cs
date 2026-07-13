using Aurelian.Core.Compositor;
using Aurelian.Runtime.Compositor;

namespace Aurelian.Core.Engine.Frames;

public sealed class AurelianFramePump
{
    private readonly AurelianEngine engine;
    private readonly CompositorActuationBridge compositorBridge;
    private readonly AurelianFramePumpOptions options;

    public AurelianFramePump(
        AurelianEngine engine,
        CompositorActuationBridge compositorBridge,
        AurelianFramePumpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(compositorBridge);

        this.engine = engine;
        this.compositorBridge = compositorBridge;
        this.options = options ?? new AurelianFramePumpOptions();
    }

    public async Task<AurelianFrameResult> RunOneFrameAsync(
        AurelianFrameInput input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return Rejected(
                AurelianFrameId.Zero,
                AurelianFrameDiagnosticCodes.MissingFrameInput,
                "Aurelian frame pump requires a frame input.");
        }

        if (options.RequireEngineStarted && engine.Status != AurelianEngineStatus.Started)
        {
            return Rejected(
                input.FrameId,
                AurelianFrameDiagnosticCodes.EngineNotStarted,
                "Aurelian frame pump cannot run a frame before the engine is started.");
        }

        if (input.CompositorFacts is null)
        {
            return Rejected(
                input.FrameId,
                AurelianFrameDiagnosticCodes.MissingCompositorFacts,
                "Aurelian frame pump requires compositor policy facts.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(input.FrameId, null);
        }

        try
        {
            CompositorPolicyResult compositorResult = await CompositorPolicySession
                .RunOnceAsync(input.CompositorFacts, compositorBridge.AsHandler(), cancellationToken)
                .ConfigureAwait(false);

            return MapCompositorResult(input.FrameId, compositorResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(input.FrameId, null);
        }
    }

    /// <summary>
    /// Keeps engine command acceptance behind the same Aurelian-owned frame
    /// boundary as frame execution; integration hosts never mutate engine state.
    /// </summary>
    public AurelianEngineResult AcceptCloseRequest(Aurelian.Core.Engine.Commands.AurelianCloseRequest request)
    {
        return engine.AcceptCloseRequest(request);
    }

    private static AurelianFrameResult MapCompositorResult(
        AurelianFrameId frameId,
        CompositorPolicyResult compositorResult)
    {
        return compositorResult.Status switch
        {
            CompositorPolicyStatus.Dispatched => compositorResult.Success
                ? new AurelianFrameResult(AurelianFrameStatus.Completed, frameId, compositorResult, [])
                : Failed(frameId, compositorResult, AurelianFrameDiagnosticCodes.CompositorFailed, "Compositor policy dispatch completed with error diagnostics."),
            CompositorPolicyStatus.WaitingForOutputs => new AurelianFrameResult(
                AurelianFrameStatus.Waiting,
                frameId,
                compositorResult,
                [new AurelianFrameDiagnostic(AurelianFrameDiagnosticCodes.CompositorWaiting, AurelianFrameDiagnosticSeverity.Info, "Compositor policy is waiting for required plant outputs.")]),
            CompositorPolicyStatus.Rejected => new AurelianFrameResult(
                AurelianFrameStatus.Rejected,
                frameId,
                compositorResult,
                [new AurelianFrameDiagnostic(AurelianFrameDiagnosticCodes.CompositorRejected, AurelianFrameDiagnosticSeverity.Error, "Compositor policy rejected the frame facts.")]),
            CompositorPolicyStatus.Failed => Failed(frameId, compositorResult, AurelianFrameDiagnosticCodes.CompositorFailed, "Compositor policy failed while running the frame."),
            _ => Failed(frameId, compositorResult, AurelianFrameDiagnosticCodes.CompositorFailed, $"Compositor policy returned unsupported status '{compositorResult.Status}'."),
        };
    }

    private static AurelianFrameResult Rejected(AurelianFrameId frameId, string code, string message) =>
        new(AurelianFrameStatus.Rejected, frameId, null, [new AurelianFrameDiagnostic(code, AurelianFrameDiagnosticSeverity.Error, message)]);

    private static AurelianFrameResult Failed(AurelianFrameId frameId, CompositorPolicyResult? compositorResult, string code, string message) =>
        new(AurelianFrameStatus.Failed, frameId, compositorResult, [new AurelianFrameDiagnostic(code, AurelianFrameDiagnosticSeverity.Error, message)]);

    private static AurelianFrameResult Cancelled(AurelianFrameId frameId, CompositorPolicyResult? compositorResult) =>
        Failed(frameId, compositorResult, AurelianFrameDiagnosticCodes.FrameCancelled, "Aurelian frame pump run was canceled.");

}
