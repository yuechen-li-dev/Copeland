using Machina.Core.Flat;
using Machina.Pipeline;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public sealed class PresenterNavigationRenderSession
{
    private PresenterCachedPageLayer? _cachedPageLayer;
    private PresenterCachedShellLayer? _cachedShellLayer;
    private int _pageRenderCount;
    private int _shellRenderCount;
    private int _compositionCount;

    public PresenterNavigationShellRenderResult Render(
        DemoState demoState,
        PresenterNavigationState navigationState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        PresenterNavigationLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(demoState);
        ArgumentNullException.ThrowIfNull(navigationState);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(proofOptions);

        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterNavigationLayout effectiveLayout = layout ?? PresenterNavigationLayout.Default;
        PresenterNavigationState normalizedState = PresenterNavigationShellRenderer.NormalizeState(
            navigationState,
            model,
            proofOptions,
            effectiveLayout);

        PresenterNavigationSection section = model.FindSection(normalizedState.SelectedSectionId) ?? model.Sections[0];
        string selectedTabId = normalizedState.GetSelectedTabId(section.Id, model);
        PresenterNavigationTab tab = model.FindTab(section.Id, selectedTabId) ?? section.Tabs[0];

        PresenterPageRenderResult pageRender = GetOrRenderPageLayer(
            demoState,
            normalizedState,
            theme,
            proofOptions,
            effectiveLayout,
            tab.PageId);

        double currentOffset = normalizedState.GetScrollOffset(tab.PageId);
        ScrollbarGeometry scrollbarGeometry = PresenterScrollRegion.ComputeScrollbarGeometry(
            effectiveLayout.ScrollbarTrackRect,
            pageRender.ContentHeight,
            effectiveLayout.ViewportHeight,
            currentOffset);

        normalizedState = normalizedState.WithScrollOffset(tab.PageId, scrollbarGeometry.ScrollOffset);
        PresenterNavigationChromeGeometry chromeGeometry = PresenterNavigationChromeGeometryBuilder.Build(
            model,
            normalizedState,
            effectiveLayout,
            section,
            scrollbarGeometry);

        MachinaFrame shellFrame = GetOrRenderShellLayer(
            model,
            normalizedState,
            chromeGeometry,
            effectiveLayout,
            theme,
            tab.PageId,
            scrollbarGeometry,
            proofOptions);

        RasterFrame composedFrame = PresenterNavigationFrameComposer.Compose(
            shellFrame.RasterFrame,
            pageRender.Frame.RasterFrame,
            effectiveLayout.ViewportRect,
            scrollbarGeometry);
        _compositionCount++;

        return new PresenterNavigationShellRenderResult(
            Model: model,
            NavigationState: normalizedState,
            Layout: effectiveLayout,
            ProofOptions: proofOptions,
            SelectedSection: section,
            SelectedTab: tab,
            ChromeGeometry: chromeGeometry,
            ShellFrame: shellFrame,
            PageFrame: pageRender.Frame,
            ComposedFrame: composedFrame,
            ScrollbarGeometry: scrollbarGeometry,
            Diagnostics: new PresenterNavigationRenderDiagnostics(
                _pageRenderCount,
                _shellRenderCount,
                _compositionCount),
            Session: this,
            PageRender: pageRender);
    }

    public PresenterNavigationRenderDiagnostics GetDiagnostics()
    {
        return new PresenterNavigationRenderDiagnostics(
            _pageRenderCount,
            _shellRenderCount,
            _compositionCount);
    }

    private PresenterPageRenderResult GetOrRenderPageLayer(
        DemoState demoState,
        PresenterNavigationState navigationState,
        StandardTheme theme,
        PresenterProofOptions proofOptions,
        PresenterNavigationLayout layout,
        string pageId)
    {
        string? selectedCardId = null;
        if (PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(pageId, proofOptions);
            selectedCardId = navigationState.GetSelectedCardId(pageId, cards);
        }

        var key = new PresenterCachedPageLayerKey(
            pageId,
            demoState,
            theme,
            proofOptions,
            layout.ContentVisibleWidth,
            selectedCardId);

        if (_cachedPageLayer is not null && Equals(_cachedPageLayer.Key, key))
        {
            return _cachedPageLayer.PageRender;
        }

        PresenterPageRenderResult pageRender = PresenterNavigationCatalog.RenderPage(
            pageId,
            demoState,
            theme,
            proofOptions,
            layout.ContentVisibleWidth,
            navigationState);

        _cachedPageLayer = new PresenterCachedPageLayer(key, pageRender);
        _pageRenderCount++;
        return pageRender;
    }

    private MachinaFrame GetOrRenderShellLayer(
        PresenterNavigationModel model,
        PresenterNavigationState navigationState,
        PresenterNavigationChromeGeometry chromeGeometry,
        PresenterNavigationLayout layout,
        StandardTheme theme,
        string selectedPageId,
        ScrollbarGeometry scrollbarGeometry,
        PresenterProofOptions proofOptions)
    {
        string selectedTabId = navigationState.GetSelectedTabId(navigationState.SelectedSectionId, model);
        var key = new PresenterCachedShellLayerKey(
            navigationState.SelectedSectionId,
            selectedTabId,
            selectedPageId,
            theme,
            proofOptions,
            layout,
            scrollbarGeometry.IsVisible);

        if (_cachedShellLayer is not null && Equals(_cachedShellLayer.Key, key))
        {
            return _cachedShellLayer.Frame;
        }

        UiDocument shellDocument = PresenterNavigationDocumentFactory.BuildShellDocument(
            model,
            navigationState,
            chromeGeometry,
            layout,
            theme,
            selectedPageId,
            scrollbarGeometry,
            proofOptions);

        MachinaFrame shellFrame = new MachinaRasterPipeline().Render(shellDocument, layout.RootWidth, layout.RootHeight);
        _cachedShellLayer = new PresenterCachedShellLayer(key, shellFrame);
        _shellRenderCount++;
        return shellFrame;
    }
}

public sealed record PresenterNavigationRenderDiagnostics(
    int PageRenderCount,
    int ShellRenderCount,
    int CompositionCount);

internal sealed record PresenterCachedPageLayerKey(
    string PageId,
    DemoState DemoState,
    StandardTheme Theme,
    PresenterProofOptions ProofOptions,
    int ContentWidth,
    string? SelectedCardId);

internal sealed record PresenterCachedPageLayer(
    PresenterCachedPageLayerKey Key,
    PresenterPageRenderResult PageRender);

internal sealed record PresenterCachedShellLayerKey(
    string SelectedSectionId,
    string SelectedTabId,
    string SelectedPageId,
    StandardTheme Theme,
    PresenterProofOptions ProofOptions,
    PresenterNavigationLayout Layout,
    bool IsScrollbarVisible);

internal sealed record PresenterCachedShellLayer(
    PresenterCachedShellLayerKey Key,
    MachinaFrame Frame);
