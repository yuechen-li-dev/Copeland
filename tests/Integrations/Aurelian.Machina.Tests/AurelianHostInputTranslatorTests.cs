using Aurelian.Core.Engine.Commands;
using Aurelian.Machina;
using Machina.Presentation.Input;
using Machina.Runtime.Input;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class AurelianHostInputTranslatorTests
{
    [Fact]
    public void TranslateLifecycle_ConsumesOnlyAurelianHostFactsFromOrderedUiBatch()
    {
        var batch = new UiInputBatch(12,
        [
            new UiPointerMoved(new PointerPoint(1, 2), null, UiModifiers.None),
            new UiSurfaceResized(new UiSurfaceSize(640, 480)),
            new UiSurfaceResized(new UiSurfaceSize(800, 600)),
            new UiCloseRequested(),
        ]);

        var lifecycle = AurelianHostInputTranslator.TranslateLifecycle(batch);

        Assert.Equal(new Aurelian.Core.Engine.Frames.AurelianHostExtent(800, 600), lifecycle.HostExtent);
        Assert.True(lifecycle.CloseRequested);
    }

    [Fact]
    public void TranslateCloseRequest_ProducesBackendOwnedRequest()
    {
        AurelianCloseRequest request = AurelianHostInputTranslator.Translate(new MachinaFrontendCloseRequested());

        Assert.NotNull(request);
    }
}
