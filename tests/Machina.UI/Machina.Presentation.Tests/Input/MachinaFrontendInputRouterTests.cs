using Machina.Presentation.Input;
using Machina.Runtime.Input;
using Xunit;

namespace Machina.Presentation.Tests.Input;

public sealed class MachinaFrontendInputRouterTests
{
    [Fact]
    public void EmptyBatch_ProducesNoLifecycleOutput()
    {
        MachinaFrontendInputRoutingResult result = MachinaFrontendInputRouter.Route(UiInputBatch.Empty(9));

        Assert.Equal((ulong)9, result.BatchId);
        Assert.Empty(result.FrontendMessages);
        Assert.False(result.RequiresRecomposition);
    }

    [Fact]
    public void OrderedMixedBatch_PreservesEveryResizeAndEmitsCloseMessagesInEventOrder()
    {
        var batch = new UiInputBatch(10,
        [
            new UiSurfaceResized(new UiSurfaceSize(640, 480)),
            new UiPointerMoved(new PointerPoint(2, 3), null, UiModifiers.None),
            new UiCloseRequested(),
            new UiSurfaceResized(new UiSurfaceSize(800, 600)),
            new UiCloseRequested(),
        ]);

        MachinaFrontendInputRoutingResult result = MachinaFrontendInputRouter.Route(batch);

        Assert.True(result.RequiresRecomposition);
        Assert.Collection(
            result.FrontendMessages,
            message => Assert.Equal(new MachinaFrontendSurfaceResized(new UiSurfaceSize(640, 480)), message),
            message => Assert.IsType<MachinaFrontendCloseRequested>(message),
            message => Assert.Equal(new MachinaFrontendSurfaceResized(new UiSurfaceSize(800, 600)), message),
            message => Assert.IsType<MachinaFrontendCloseRequested>(message));
    }

    [Fact]
    public void EventsAfterClose_AreStillObservedInTheSameBatch()
    {
        var batch = new UiInputBatch(11,
        [
            new UiCloseRequested(),
            new UiSurfaceResized(new UiSurfaceSize(1280, 720)),
        ]);

        MachinaFrontendInputRoutingResult result = MachinaFrontendInputRouter.Route(batch);

        Assert.Single(result.FrontendMessages.OfType<MachinaFrontendCloseRequested>());
        Assert.Collection(
            result.FrontendMessages,
            message => Assert.IsType<MachinaFrontendCloseRequested>(message),
            message => Assert.Equal(new MachinaFrontendSurfaceResized(new UiSurfaceSize(1280, 720)), message));
    }
}
