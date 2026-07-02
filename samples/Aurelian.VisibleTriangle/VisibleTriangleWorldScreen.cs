using Aurelian.Core.Engine.Frames;
using Aurelian.Core.Engine.Runtime;
using Aurelian.Core.Presentation.Screens;

namespace Aurelian.VisibleTriangle;

internal sealed class VisibleTriangleWorldScreen : IPresenterScreen
{
    private readonly VisibleTriangleSampleFrame sampleFrame;

    public VisibleTriangleWorldScreen(VisibleTriangleSampleFrame sampleFrame, bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(sampleFrame);
        this.sampleFrame = sampleFrame;
        IsVisible = isVisible;
    }

    public ScreenLayerKey Layer => ScreenLayers.World.Key;

    public bool IsVisible { get; }

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
