using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Runtime;

namespace Aurelian.VisibleTriangle;

internal sealed class VisibleTriangleWorldScreen
{
    private readonly VisibleTriangleSampleFrame sampleFrame;

    public VisibleTriangleWorldScreen(VisibleTriangleSampleFrame sampleFrame)
    {
        ArgumentNullException.ThrowIfNull(sampleFrame);
        this.sampleFrame = sampleFrame;
    }

    public VisibleTriangleSampleFrame SampleFrame => sampleFrame;

    public Task<AurelianFrameLoopResult> RunFrameLoopAsync(
        AurelianRuntimeTickFrameStep runtimeTickStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeTickStep);

        var frameLoop = new AurelianFrameLoop(
            sampleFrame.FramePump,
            sampleFrame.InputProvider,
            sampleFrame.PresentationMechanism,
            new AurelianFrameLoopOptions(
                MaxFrames: sampleFrame.PlannedFrameCount,
                PresentAfterCompletedFrame: true,
                StopOnFrameFailure: true),
            runtimeTickStep);

        return frameLoop.RunAsync(sampleFrame.StartFrameId, cancellationToken);
    }
}
