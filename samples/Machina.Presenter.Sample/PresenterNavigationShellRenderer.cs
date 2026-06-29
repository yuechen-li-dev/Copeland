using Machina.Core.Actions;
using Machina.Core.Flat;
using Machina.Layout.Geometry;
using Machina.Pipeline;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Surface;
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
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(proofOptions);

        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterNavigationLayout layout = PresenterNavigationLayout.Default;
        PresenterNavigationState normalizedState = NormalizeState(navigationState, model, proofOptions, layout);

        PresenterNavigationSection section = model.FindSection(normalizedState.SelectedSectionId) ?? model.Sections[0];
        string selectedTabId = normalizedState.GetSelectedTabId(section.Id, model);
        PresenterNavigationTab tab = model.FindTab(section.Id, selectedTabId) ?? section.Tabs[0];

        PresenterPageRenderResult pageRender = PresenterNavigationCatalog.RenderPage(
            tab.PageId,
            demoState,
            theme,
            proofOptions,
            layout.ContentVisibleWidth);

        double currentOffset = normalizedState.GetScrollOffset(tab.PageId);
        ScrollbarGeometry scrollbarGeometry = PresenterScrollRegion.ComputeScrollbarGeometry(
            layout.ScrollbarTrackRect,
            pageRender.ContentHeight,
            layout.ViewportHeight,
            currentOffset);

        normalizedState = normalizedState.WithScrollOffset(tab.PageId, scrollbarGeometry.ScrollOffset);
        PresenterNavigationChromeGeometry chromeGeometry = PresenterNavigationChromeGeometryBuilder.Build(
            model,
            normalizedState,
            layout,
            section,
            scrollbarGeometry);

        UiDocument shellDocument = PresenterNavigationDocumentFactory.BuildShellDocument(
            model,
            normalizedState,
            chromeGeometry,
            layout,
            theme,
            tab.PageId,
            scrollbarGeometry,
            proofOptions);

        MachinaFrame shellFrame = new MachinaRasterPipeline().Render(shellDocument, layout.RootWidth, layout.RootHeight);
        RasterFrame composedFrame = ComposeFrame(shellFrame.RasterFrame, pageRender.Frame.RasterFrame, layout.ViewportRect, scrollbarGeometry.ScrollOffset);

        return new PresenterNavigationShellRenderResult(
            Model: model,
            NavigationState: normalizedState,
            Layout: layout,
            SelectedSection: section,
            SelectedTab: tab,
            ChromeGeometry: chromeGeometry,
            ShellFrame: shellFrame,
            PageFrame: pageRender.Frame,
            ComposedFrame: composedFrame,
            ScrollbarGeometry: scrollbarGeometry);
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

    private static RasterFrame ComposeFrame(RasterFrame shellFrame, RasterFrame pageFrame, Rect viewportRect, double scrollOffset)
    {
        RasterSurface composedSurface = CloneSurface(shellFrame.Surface);
        BlitPageContent(pageFrame.Surface, composedSurface, viewportRect, scrollOffset);
        return new RasterFrame(shellFrame.Width, shellFrame.Height, composedSurface);
    }

    private static RasterSurface CloneSurface(RasterSurface source)
    {
        var clone = new RasterSurface(source.Width, source.Height);
        Array.Copy(source.Pixels, clone.Pixels, source.Pixels.Length);
        return clone;
    }

    private static void BlitPageContent(RasterSurface source, RasterSurface destination, Rect viewportRect, double scrollOffset)
    {
        int sourceTop = Math.Max(0, (int)Math.Floor(scrollOffset));
        int viewportLeft = (int)Math.Floor(viewportRect.X);
        int viewportTop = (int)Math.Floor(viewportRect.Y);
        int viewportWidth = Math.Min((int)Math.Floor(viewportRect.Width), source.Width);
        int viewportHeight = (int)Math.Floor(viewportRect.Height);

        for (int y = 0; y < viewportHeight; y++)
        {
            int sourceY = sourceTop + y;
            if (sourceY < 0 || sourceY >= source.Height)
            {
                continue;
            }

            int destinationY = viewportTop + y;
            if (destinationY < 0 || destinationY >= destination.Height)
            {
                continue;
            }

            for (int x = 0; x < viewportWidth; x++)
            {
                int sourceX = x;
                if (sourceX < 0 || sourceX >= source.Width)
                {
                    continue;
                }

                int destinationX = viewportLeft + x;
                if (destinationX < 0 || destinationX >= destination.Width)
                {
                    continue;
                }

                Rgba32 pixel = source.GetPixel(sourceX, sourceY);
                if (pixel.A == 0)
                {
                    continue;
                }

                destination.SetPixel(destinationX, destinationY, pixel);
            }
        }
    }
}

public sealed record PresenterNavigationShellRenderResult(
    PresenterNavigationModel Model,
    PresenterNavigationState NavigationState,
    PresenterNavigationLayout Layout,
    PresenterNavigationSection SelectedSection,
    PresenterNavigationTab SelectedTab,
    PresenterNavigationChromeGeometry ChromeGeometry,
    MachinaFrame ShellFrame,
    MachinaFrame PageFrame,
    RasterFrame ComposedFrame,
    ScrollbarGeometry ScrollbarGeometry)
{
    public UiAction? HitTestContent(PointerPoint rootPoint)
    {
        Rect viewport = Layout.ViewportRect;
        if (rootPoint.X < viewport.X ||
            rootPoint.Y < viewport.Y ||
            rootPoint.X >= viewport.X + viewport.Width ||
            rootPoint.Y >= viewport.Y + viewport.Height)
        {
            return null;
        }

        double contentX = rootPoint.X - viewport.X;
        double contentY = rootPoint.Y - viewport.Y + ScrollbarGeometry.ScrollOffset;
        var hit = PageFrame.HitTest.HitTest(new PointerPoint(contentX, contentY));
        return hit?.Action;
    }
}
