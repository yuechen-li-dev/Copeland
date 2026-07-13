using Aurelian.Machina;
using Machina.Runtime.Input;
using Xunit;

namespace Aurelian.VisibleTriangle.Tests;

public sealed class VisibleTriangleHostInputCollectorTests
{
    [Fact]
    public void InitialHostIteration_NormalizesExtentBeforeCloseAndFansOutLifecycle()
    {
        UiInputBatch batch = VisibleTriangleHostInputCollector.Collect(
            batchId: 3,
            includeInitialExtent: true,
            width: 640,
            height: 480,
            closeRequested: true);

        Assert.Collection(
            batch.Events,
            inputEvent => Assert.IsType<UiSurfaceResized>(inputEvent),
            inputEvent => Assert.IsType<UiCloseRequested>(inputEvent));

        var lifecycle = AurelianHostInputTranslator.TranslateLifecycle(batch);
        Assert.Equal(new Aurelian.Core.Engine.Frames.AurelianHostExtent(640, 480), lifecycle.HostExtent);
        Assert.True(lifecycle.CloseRequested);
    }
}
