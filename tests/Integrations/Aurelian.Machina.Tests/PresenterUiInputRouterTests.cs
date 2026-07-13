using Machina.Presenter.Sample;
using Machina.Presentation.Input;
using Machina.Runtime.Input;
using Machina.Standard.Theme;
using Xunit;

namespace Aurelian.Machina.Tests;

public sealed class PresenterUiInputRouterTests
{
    [Fact]
    public void OrderedBatch_RoutesFoundationalEventsAndRetainsFrontendLifecycleOutput()
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterProofOptions proofOptions = new();
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(
            1280,
            720,
            PresenterShellMode.Wide);
        PresenterNavigationState state = PresenterNavigationCatalog.CreateState(
            model,
            proofOptions,
            PresenterNavigationExportOptions.DefaultShell);
        PresenterNavigationShellRenderResult render = new PresenterNavigationRenderSession().Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            proofOptions,
            layout);

        var batch = new UiInputBatch(42,
        [
            new UiSurfaceResized(new UiSurfaceSize(1280, 720)),
            new UiPointerMoved(new PointerPoint(layout.ContentLeft + 20, layout.ContentTop + 120), null, UiModifiers.None),
            new UiPointerButtonChanged(new PointerPoint(layout.ContentLeft + 20, layout.ContentTop + 120), UiPointerButton.Primary, true, UiModifiers.None),
            new UiPointerWheel(new PointerPoint(layout.ContentLeft + 20, layout.ContentTop + 120), 0, 1, UiModifiers.None),
            new UiCloseRequested(),
        ]);

        PresenterUiInputRoutingResult result = PresenterUiInputRouter.Route(
            render,
            batch,
            PresenterScrollbarInteractionState.Default);

        Assert.Equal((ulong)42, result.BatchId);
        Assert.Equal(3, result.RoutedEvents.Length);
        Assert.True(result.RequiresRecomposition);
        Assert.True(result.CloseRequested);
        Assert.Equal(2, result.FrontendMessages.Length);
        Assert.Single(result.FrontendMessages.OfType<MachinaFrontendCloseRequested>());
    }

    [Fact]
    public void ResizeInBatch_RecomposesBeforeLaterPointerRouting()
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterProofOptions proofOptions = new();
        PresenterNavigationState state = PresenterNavigationCatalog.CreateState(
            model,
            proofOptions,
            PresenterNavigationExportOptions.DefaultShell);
        PresenterNavigationLayout initialLayout = PresenterNavigationLayout.Create(1280, 720, PresenterShellMode.Wide);
        PresenterNavigationShellRenderResult initialRender = Render(state, proofOptions, initialLayout);
        int recompositionCount = 0;
        UiSurfaceSize? recomposedSize = null;

        var batch = new UiInputBatch(43,
        [
            new UiSurfaceResized(new UiSurfaceSize(800, 600)),
            new UiPointerMoved(new PointerPoint(200, 200), null, UiModifiers.None),
        ]);

        PresenterUiInputRoutingResult result = PresenterUiInputRouter.Route(
            initialRender,
            batch,
            PresenterScrollbarInteractionState.Default,
            size =>
            {
                recompositionCount++;
                recomposedSize = size;
                PresenterNavigationLayout resizedLayout = PresenterNavigationLayout.Create(
                    size.Width,
                    size.Height,
                    PresenterShellMode.Compact);
                return Render(state, proofOptions, resizedLayout);
            });

        Assert.Equal(1, recompositionCount);
        Assert.Equal(1, result.RecompositionCount);
        Assert.Equal(new UiSurfaceSize(800, 600), recomposedSize);
        Assert.Single(result.RoutedEvents);
        Assert.True(result.RequiresRecomposition);
    }

    private static PresenterNavigationShellRenderResult Render(
        PresenterNavigationState state,
        PresenterProofOptions proofOptions,
        PresenterNavigationLayout layout)
    {
        return new PresenterNavigationRenderSession().Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            proofOptions,
            layout);
    }
}
