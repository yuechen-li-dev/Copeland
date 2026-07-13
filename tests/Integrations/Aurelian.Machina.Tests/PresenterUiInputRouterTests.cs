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
}
