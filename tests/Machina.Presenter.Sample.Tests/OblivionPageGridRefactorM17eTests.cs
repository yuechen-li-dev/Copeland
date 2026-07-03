using System.Text.Json;
using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Presenter.Sample;
using Machina.Presenter.Sample.Playback;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionPageGridRefactorM17eTests
{
    private const string DocsPageId = "oblivion.docs";
    private const string ExpandedDocCardId = "doc-aurelian-build-topology-m13b";
    private const string AlternateDocCardId = "doc-copeland-markdown-frontend-m12a";
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly StandardTheme Theme = StandardTheme.Default;
    private static readonly PresenterProofOptions ProofOptions = new();
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void OblivionWidePage_UsesGridForCardsAndInspectorPanes()
    {
        PresenterPageRenderResult page = RenderWideDocsPage();

        Assert.NotNull(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid"));
        Assert.IsType<GridArrange>(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid").Arrange);
    }

    [Fact]
    public void OblivionWidePage_GridHasFillCardsColumnAndFixedInspectorColumn()
    {
        PresenterNavigationLayout layout = CreateWideShellLayout(1280, 720);
        PresenterPageRenderResult page = RenderWideDocsPage(layout);
        GridArrange arrange = Assert.IsType<GridArrange>(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid").Arrange);

        Assert.Collection(
            arrange.Columns,
            column => Assert.IsType<FillGridTrack>(column),
            column =>
            {
                FixedGridTrack fixedColumn = Assert.IsType<FixedGridTrack>(column);
                Assert.Equal(OblivionPageLayout.CreateWide(layout.ContentVisibleWidth, layout.ViewportHeight).InspectorWidth, fixedColumn.Size);
            });
    }

    [Fact]
    public void OblivionWidePage_GridUsesColumnGap()
    {
        PresenterNavigationLayout layout = CreateWideShellLayout(1280, 720);
        PresenterPageRenderResult page = RenderWideDocsPage(layout);
        ResolvedLayoutDocument resolved = page.Frame.Resolved;
        Rect leftCell = FindRectBySuffix(resolved, $"{DocsPageId}.page-grid.cell-0-0");
        Rect rightCell = FindRectBySuffix(resolved, $"{DocsPageId}.page-grid.cell-0-1");
        OblivionPageLayout pageLayout = OblivionPageLayout.CreateWide(layout.ContentVisibleWidth, layout.ViewportHeight);

        GridArrange arrange = Assert.IsType<GridArrange>(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid").Arrange);

        Assert.Equal(pageLayout.PageGap, arrange.ColumnGap);
        Assert.Equal(pageLayout.PageGap, rightCell.X - (leftCell.X + leftCell.Width));
    }

    [Fact]
    public void OblivionWidePage_CardsPaneInLeftCell()
    {
        PresenterPageRenderResult page = RenderWideDocsPage();
        CellFrame frame = Assert.IsType<CellFrame>(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid.cell-0-0").Frame);

        Assert.Equal(0, frame.Row);
        Assert.Equal(0, frame.Column);
        Assert.NotNull(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.cards-panel"));
    }

    [Fact]
    public void OblivionWidePage_InspectorPaneInRightCell()
    {
        PresenterPageRenderResult page = RenderWideDocsPage();
        CellFrame frame = Assert.IsType<CellFrame>(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid.cell-0-1").Frame);

        Assert.Equal(0, frame.Row);
        Assert.Equal(1, frame.Column);
        Assert.NotNull(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.inspector-panel"));
    }

    [Fact]
    public void OblivionWidePage_DerivesStableGridCellIds()
    {
        PresenterPageRenderResult page = RenderWideDocsPage();

        Assert.NotNull(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid.cell-0-0"));
        Assert.NotNull(FindRowBySuffix(page.Frame.Lowering.Rows, $"{DocsPageId}.page-grid.cell-0-1"));
    }

    [Fact]
    public void OblivionWidePage_MainStackRegionStillExists()
    {
        PresenterNavigationShellRenderResult render = RenderWideShell(CreateDocsState());

        Assert.Contains(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            region => region.Target.Kind == PresenterScrollbarTargetKind.OblivionMainCardStack);
    }

    [Fact]
    public void OblivionWidePage_InspectorRegionStillExists()
    {
        PresenterNavigationShellRenderResult render = RenderWideShell(CreateDocsState());

        Assert.Contains(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            region => region.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorPane);
    }

    [Fact]
    public void OblivionWidePage_MainAndInspectorScrollOffsetsRemainIndependent()
    {
        PresenterNavigationState state = CreateDocsState(
            selectedCardId: ExpandedDocCardId,
            expandedCardId: ExpandedDocCardId,
            mainScrollOffset: 180,
            inspectorScrollOffset: 240);

        PresenterNavigationState next = PresenterNavigationDispatch.Dispatch(
            state,
            PresenterNavigationActions.SetOblivionInspectorScrollOffset(DocsPageId, 320),
            Model,
            ProofOptions,
            CreateWideShellLayout(1280, 720));

        Assert.Equal(180, next.GetScrollOffset(DocsPageId));
        Assert.True(next.GetInspectorScrollOffset(DocsPageId) > 240);
    }

    [Fact]
    public void OblivionWidePage_SelectingCardStillUpdatesInspector()
    {
        PresenterNavigationState state = CreateDocsState(
            selectedCardId: ExpandedDocCardId,
            expandedCardId: ExpandedDocCardId,
            inspectorScrollOffset: 220);

        PresenterNavigationState next = PresenterNavigationDispatch.Dispatch(
            state,
            PresenterNavigationActions.SelectOblivionCard(DocsPageId, AlternateDocCardId),
            Model,
            ProofOptions,
            CreateWideShellLayout(1280, 720));

        PresenterNavigationShellRenderResult render = RenderWideShell(next);
        OblivionScrollRegionTarget rawSource = Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            region => region.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource);

        Assert.Equal(AlternateDocCardId, next.GetSelectedCardId(DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(DocsPageId, ProofOptions)));
        Assert.Equal(AlternateDocCardId, rawSource.Target.CardId);
        Assert.Equal(0, next.GetInspectorScrollOffset(DocsPageId));
    }

    [Fact]
    public void OblivionWidePage_ExpandedMarkdownStillRendersInline()
    {
        PresenterPageRenderResult page = RenderWideDocsPage(CreateDocsState(
            selectedCardId: ExpandedDocCardId,
            expandedCardId: ExpandedDocCardId));

        Assert.Contains(
            page.Frame.RenderCommands.OfType<PushClipCommand>(),
            command => command.Id.Contains($"{ExpandedDocCardId}.expanded-body-viewport", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionWidePage_RawSourceStillScrolls()
    {
        PresenterNavigationShellRenderResult render = RenderWideShell(CreateDocsState(
            selectedCardId: ExpandedDocCardId,
            expandedCardId: ExpandedDocCardId,
            inspectorScrollOffset: 240));
        OblivionScrollRegionTarget rawSource = Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            region => region.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource &&
                string.Equals(region.Target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        OblivionPageInteractionRoutingResult routed = render.PageRender.OblivionInteraction.RouteInput(
            Wheel(Center(rawSource.Bounds), -1),
            render.ScrollbarGeometry.ScrollOffset,
            PresenterScrollbarInteractionState.Default);

        Assert.NotNull(routed.Action);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionRawMarkdownSourceScrollOffset(
            routed.Action!.Id,
            out string pageId,
            out string cardId,
            out double offset));
        Assert.Equal(DocsPageId, pageId);
        Assert.Equal(ExpandedDocCardId, cardId);
        Assert.True(offset > 0);
    }

    [Fact]
    public void OblivionCompactPage_BehaviorStillWorks()
    {
        PresenterNavigationState state = CreateDocsState(selectedCardId: ExpandedDocCardId, expandedCardId: ExpandedDocCardId);
        PresenterNavigationShellRenderResult render = RenderCompactShell(state);

        Assert.DoesNotContain(render.PageRender!.Document.Rows, row => row.Id == new NodeId($"{DocsPageId}.page-grid"));
        Assert.Contains(render.PageRender.Document.Rows, row => row.Id == new NodeId($"{DocsPageId}.compact-title"));
    }

    [Fact]
    public void OblivionCompactPage_ExpandedCardStillRenders()
    {
        PresenterPageRenderResult page = RenderCompactDocsPage(CreateDocsState(
            selectedCardId: ExpandedDocCardId,
            expandedCardId: ExpandedDocCardId));

        Assert.Contains(
            page.Frame.RenderCommands.OfType<DrawTextCommand>(),
            command => command.Id.Contains($"{ExpandedDocCardId}.title", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionCompactPage_PlaybackTargetsStillResolve()
    {
        PresenterNavigationShellRenderResult render = RenderCompactShell(CreateDocsState(
            selectedCardId: ExpandedDocCardId,
            expandedCardId: ExpandedDocCardId));

        PresenterPlaybackResolvedTarget target = PresenterPlaybackTargetResolver.Resolve(
            render,
            "card-header",
            ExpandedDocCardId);

        Assert.Equal("card-header", target.Name);
        Assert.Equal(ExpandedDocCardId, target.CardId);
    }

    [Fact]
    public void M17e_DoesNotImplementProportionalUiLength()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("proportionalUiLengthImplemented").GetBoolean());
    }

    [Fact]
    public void M17e_DoesNotImplementGuideFrame()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("guideFrameImplemented").GetBoolean());
    }

    [Fact]
    public void M17e_DoesNotImplementRowVariants()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("rowVariantsImplemented").GetBoolean());
    }

    [Fact]
    public void M17e_DoesNotImplementDeusMachine()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("deusMachineImplemented").GetBoolean());
    }

    [Fact]
    public void M17e_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M17e_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadMilestoneManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    private static PresenterNavigationState CreateDocsState(
        string? selectedCardId = null,
        string? expandedCardId = null,
        double mainScrollOffset = 0,
        double inspectorScrollOffset = 0)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs")
            .WithScrollOffset(DocsPageId, mainScrollOffset)
            .WithInspectorScrollOffset(DocsPageId, inspectorScrollOffset);

        if (!string.IsNullOrWhiteSpace(selectedCardId))
        {
            state = state.WithSelectedCard(DocsPageId, selectedCardId);
        }

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state.WithCardViewState(DocsPageId, expandedCardId, new OblivionCardViewState(true, 0));
        }

        return state;
    }

    private static PresenterNavigationLayout CreateWideShellLayout(int width, int height)
    {
        return PresenterNavigationLayout.Create(width, height, PresenterShellMode.Wide);
    }

    private static PresenterPageRenderResult RenderWideDocsPage(
        PresenterNavigationState? state = null)
    {
        return RenderWideDocsPage(CreateWideShellLayout(1280, 720), state);
    }

    private static PresenterPageRenderResult RenderWideDocsPage(
        PresenterNavigationLayout layout,
        PresenterNavigationState? state = null)
    {
        return PresenterNavigationCatalog.RenderPage(
            DocsPageId,
            DemoState.Default,
            Theme,
            ProofOptions,
            layout.ContentVisibleWidth,
            layout.ViewportHeight,
            state ?? CreateDocsState(AlternateDocCardId),
            PresenterShellMode.Wide);
    }

    private static PresenterPageRenderResult RenderCompactDocsPage(PresenterNavigationState? state = null)
    {
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(960, 540, PresenterShellMode.Compact);
        return PresenterNavigationCatalog.RenderPage(
            DocsPageId,
            DemoState.Default,
            Theme,
            ProofOptions,
            layout.ContentVisibleWidth,
            layout.ViewportHeight,
            state ?? CreateDocsState(AlternateDocCardId),
            PresenterShellMode.Compact);
    }

    private static PresenterNavigationShellRenderResult RenderWideShell(PresenterNavigationState state)
    {
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            Theme,
            ProofOptions,
            session: null,
            CreateWideShellLayout(1280, 720));
    }

    private static PresenterNavigationShellRenderResult RenderCompactShell(PresenterNavigationState state)
    {
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            Theme,
            ProofOptions,
            session: null,
            PresenterNavigationLayout.Create(960, 540, PresenterShellMode.Compact));
    }

    private static PresenterInputEvent Wheel(PresenterInputPoint point, float delta)
    {
        return new PresenterInputEvent(PresenterInputKind.Wheel, point, WheelDeltaY: delta);
    }

    private static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static JsonDocument LoadMilestoneManifest()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "artifacts",
            "m17e",
            "oblivion-page-grid-refactor-manifest.json")));
    }

    private static LayoutRow FindRowBySuffix(IReadOnlyList<LayoutRow> rows, string suffix)
    {
        return Assert.Single(rows, row => row.Id.Value.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static Rect FindRectBySuffix(ResolvedLayoutDocument resolved, string suffix)
    {
        KeyValuePair<NodeId, ResolvedLayoutNode> match = Assert.Single(
            resolved.Nodes,
            pair => pair.Key.Value.EndsWith(suffix, StringComparison.Ordinal));
        return match.Value.Rect;
    }
}

[Collection(PlaybackXunitCollection.Name)]
public sealed class OblivionPageGridRefactorM17ePlaybackTests
{
    [Fact]
    public void M17e_PlaybackStarterScenariosStillPass()
    {
        Assert.All(PlaybackScenarioDiscovery.StarterScenarios(), scenarioFile => PlaybackScenarioXunitRunner.AssertScenarioPasses(
            scenarioFile,
            $"{nameof(OblivionPageGridRefactorM17ePlaybackTests)}.{nameof(M17e_PlaybackStarterScenariosStillPass)}"));
    }

    [Fact]
    public void M17e_PlaybackRegressionScenariosStillPass()
    {
        Assert.All(PlaybackScenarioDiscovery.RegressionScenarios(), scenarioFile => PlaybackScenarioXunitRunner.AssertScenarioPasses(
            scenarioFile,
            $"{nameof(OblivionPageGridRefactorM17ePlaybackTests)}.{nameof(M17e_PlaybackRegressionScenariosStillPass)}"));
    }
}
