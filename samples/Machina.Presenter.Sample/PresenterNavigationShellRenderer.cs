using Machina.Core.Actions;
using Machina.Runtime.Input;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationShellRenderer
{
    public static PresenterNavigationShellRenderResult Render(
        DemoState demoState,
        PresenterNavigationState navigationState,
        StandardTheme theme,
        PresenterProofOptions proofOptions)
    {
        return Render(demoState, navigationState, theme, proofOptions, session: null, layout: null);
    }

    public static PresenterNavigationShellRenderResult Render(
        DemoState demoState,
        PresenterNavigationState navigationState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        PresenterNavigationLayout layout)
    {
        return Render(demoState, navigationState, theme, proofOptions, session: null, layout);
    }

    public static PresenterNavigationShellRenderResult Render(
        DemoState demoState,
        PresenterNavigationState navigationState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        PresenterNavigationRenderSession? session = null,
        PresenterNavigationLayout? layout = null)
    {
        PresenterNavigationRenderSession effectiveSession = session ?? new PresenterNavigationRenderSession();
        return effectiveSession.Render(demoState, navigationState, theme, proofOptions, layout);
    }

    public static PresenterNavigationState NormalizeState(
        PresenterNavigationState? navigationState,
        PresenterNavigationModel model,
        PresenterProofOptions proofOptions,
        PresenterNavigationLayout layout)
    {
        PresenterNavigationState normalized = navigationState ?? PresenterNavigationState.CreateDefault(model);
        PresenterNavigationSection section = model.FindSection(normalized.SelectedSectionId) ?? model.Sections[0];
        string tabId = normalized.GetSelectedTabId(section.Id, model);
        PresenterNavigationTab tab = model.FindTab(section.Id, tabId) ?? section.Tabs[0];
        double contentHeight = PresenterNavigationCatalog.GetPageContentHeight(tab.PageId, proofOptions);
        double clampedOffset = PresenterScrollRegion.ClampScrollOffset(contentHeight, layout.ViewportHeight, normalized.GetScrollOffset(tab.PageId));

        return normalized
            .WithSelectedTab(section.Id, tab.Id)
            .WithSelectedSection(section.Id)
            .WithScrollOffset(tab.PageId, clampedOffset);
    }
}

public sealed record PresenterNavigationShellRenderResult(
    PresenterNavigationModel Model,
    PresenterNavigationState NavigationState,
    PresenterNavigationLayout Layout,
    PresenterProofOptions ProofOptions,
    PresenterNavigationSection SelectedSection,
    PresenterNavigationTab SelectedTab,
    PresenterNavigationChromeGeometry ChromeGeometry,
    Machina.Pipeline.MachinaFrame ShellFrame,
    Machina.Pipeline.MachinaFrame PageFrame,
    Machina.Renderer.Raster.Dominatus.Models.RasterFrame ComposedFrame,
    ScrollbarGeometry ScrollbarGeometry,
    PresenterNavigationRenderDiagnostics Diagnostics,
    PresenterNavigationRenderSession Session,
    PresenterPageRenderResult? PageRender = null)
{
    public UiAction? HitTestContent(PointerPoint rootPoint)
    {
        var viewport = Layout.ViewportRect;
        if (rootPoint.X < viewport.X ||
            rootPoint.Y < viewport.Y ||
            rootPoint.X >= viewport.X + viewport.Width ||
            rootPoint.Y >= viewport.Y + viewport.Height)
        {
            return null;
        }

        double contentX = rootPoint.X - viewport.X;
        double contentY = rootPoint.Y - viewport.Y + ScrollbarGeometry.ScrollOffset;
        UiHitTestResult? hit = PageFrame.HitTest.HitTest(new PointerPoint(contentX, contentY));
        if (hit?.Action is not null)
        {
            return hit.Action;
        }

        if (PageRender?.OblivionInteraction is not null)
        {
            return PageRender.OblivionInteraction.HitTest(
                new PresenterInputPoint((float)contentX, (float)(contentY - ScrollbarGeometry.ScrollOffset)),
                ScrollbarGeometry.ScrollOffset);
        }

        return hit?.Action;
    }
}
