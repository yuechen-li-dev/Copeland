using System.Text.Json;
using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionIndependentScrollPanesM15eTests
{
    private const string ExpandedDocCardId = "doc-aurelian-build-topology-m13b";
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();

    [Fact]
    public void OblivionScrollPanes_MainStackAndInspectorHaveSeparateOffsets()
    {
        PresenterNavigationState state = CreateDocsState(
            expandedCardId: ExpandedDocCardId,
            mainScrollOffset: 180,
            inspectorScrollOffset: 240);

        Assert.Equal(180, state.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
        Assert.Equal(240, state.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void OblivionScrollPanes_SelectingCardUpdatesInspectorWithoutChangingMainScroll()
    {
        PresenterNavigationState state = CreateDocsState(
            selectedCardId: ExpandedDocCardId,
            expandedCardId: ExpandedDocCardId,
            mainScrollOffset: 220,
            inspectorScrollOffset: 180);

        PresenterNavigationState next = Dispatch(
            state,
                PresenterNavigationActions.SelectOblivionCard(
                    OblivionWorkbenchCatalog.DocsPageId,
                    "doc-copeland-markdown-frontend-m12a"));

        Assert.Equal(220, next.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
        Assert.Equal("doc-copeland-markdown-frontend-m12a", next.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, GetDocsCards()));
    }

    [Fact]
    public void OblivionScrollPanes_MainScrollDoesNotChangeInspectorScroll()
    {
        PresenterNavigationState next = Dispatch(
            CreateDocsState(inspectorScrollOffset: 240),
            PresenterNavigationActions.SetScrollOffset(OblivionWorkbenchCatalog.DocsPageId, 180));

        Assert.Equal(240, next.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void OblivionScrollPanes_InspectorScrollDoesNotChangeMainScroll()
    {
        PresenterNavigationState next = Dispatch(
            CreateDocsState(mainScrollOffset: 180),
            PresenterNavigationActions.SetOblivionInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId, 240));

        Assert.Equal(180, next.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
        Assert.True(next.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId) > 0);
    }

    [Fact]
    public void OblivionScrollPanes_SelectedCardChangeResetsInspectorScrollDeterministically()
    {
        PresenterNavigationState next = Dispatch(
            CreateDocsState(selectedCardId: ExpandedDocCardId, inspectorScrollOffset: 240),
                PresenterNavigationActions.SelectOblivionCard(
                    OblivionWorkbenchCatalog.DocsPageId,
                    "doc-copeland-markdown-frontend-m12a"));

        Assert.Equal(0, next.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void OblivionInspector_RawSourceWheelScrollsSource()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240));
        OblivionScrollRegionTarget rawSource = Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            target => target.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource &&
                string.Equals(target.Target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        OblivionPageInteractionRoutingResult routed = render.PageRender.OblivionInteraction.RouteInput(
            Wheel(Center(rawSource.Bounds), -1),
            render.ScrollbarGeometry.ScrollOffset,
            PresenterScrollbarInteractionState.Default);

        Assert.True(routed.Consumed);
        Assert.NotNull(routed.Action);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionRawMarkdownSourceScrollOffset(
            routed.Action!.Id,
            out string pageId,
            out string cardId,
            out double scrollOffset));
        Assert.Equal(OblivionWorkbenchCatalog.DocsPageId, pageId);
        Assert.Equal(ExpandedDocCardId, cardId);
        Assert.True(scrollOffset > 0);
    }

    [Fact]
    public void OblivionInspector_RawSourceScrollbarDragUpdatesOffset()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240));
        OblivionScrollRegionTarget rawSource = Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            target => target.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource &&
                string.Equals(target.Target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        PresenterScrollbarInteractionState state = PresenterScrollbarInteractionState.Default;
        OblivionPageInteractionRoutingResult press = render.PageRender.OblivionInteraction.RouteInput(
            PointerPress(Center(rawSource.ScrollbarGeometry.ThumbRect)),
            render.ScrollbarGeometry.ScrollOffset,
            state);
        state = press.InteractionState;
        OblivionPageInteractionRoutingResult move = render.PageRender.OblivionInteraction.RouteInput(
            PointerMove(OffsetPoint(Center(rawSource.ScrollbarGeometry.ThumbRect), 0, 60)),
            render.ScrollbarGeometry.ScrollOffset,
            state);
        OblivionPageInteractionRoutingResult release = render.PageRender.OblivionInteraction.RouteInput(
            PointerRelease(OffsetPoint(Center(rawSource.ScrollbarGeometry.ThumbRect), 0, 60)),
            render.ScrollbarGeometry.ScrollOffset,
            move.InteractionState);

        Assert.Equal(PresenterPointerCaptureRequest.Capture, press.PointerCaptureRequest);
        Assert.NotNull(move.Action);
        Assert.Equal(PresenterPointerCaptureRequest.Release, release.PointerCaptureRequest);
    }

    [Fact]
    public void ExpandedMarkdownBody_ScrollbarThumbDragUpdatesOffset()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId));
        OblivionScrollRegionTarget body = Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            target => target.Target.Kind == PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody &&
                string.Equals(target.Target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        OblivionPageInteractionRoutingResult press = render.PageRender.OblivionInteraction.RouteInput(
            PointerPress(Center(body.ScrollbarGeometry.ThumbRect)),
            render.ScrollbarGeometry.ScrollOffset,
            PresenterScrollbarInteractionState.Default);
        OblivionPageInteractionRoutingResult move = render.PageRender.OblivionInteraction.RouteInput(
            PointerMove(OffsetPoint(Center(body.ScrollbarGeometry.ThumbRect), 0, 70)),
            render.ScrollbarGeometry.ScrollOffset,
            press.InteractionState);

        Assert.Equal(PresenterPointerCaptureRequest.Capture, press.PointerCaptureRequest);
        Assert.NotNull(move.Action);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionCardBodyScrollOffset(
            move.Action!.Id,
            out _,
            out string cardId,
            out double offset));
        Assert.Equal(ExpandedDocCardId, cardId);
        Assert.True(offset > 0);
    }

    [Fact]
    public void ScrollRouting_WheelRoutesToDeepestScrollableRegion()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240));
        OblivionScrollRegionTarget rawSource = Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            target => target.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource &&
                string.Equals(target.Target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        OblivionPageInteractionRoutingResult routed = render.PageRender.OblivionInteraction.RouteInput(
            Wheel(Center(rawSource.Bounds), -1),
            render.ScrollbarGeometry.ScrollOffset,
            PresenterScrollbarInteractionState.Default);

        Assert.True(PresenterNavigationActions.TryParseSetOblivionRawMarkdownSourceScrollOffset(
            routed.Action!.Id,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void ScrollRouting_WheelOverInspectorDoesNotScrollMainStack()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId));
        OblivionScrollRegionTarget inspector = Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            target => target.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorPane);

        OblivionPageInteractionRoutingResult routed = render.PageRender.OblivionInteraction.RouteInput(
            Wheel(Center(inspector.Bounds), -1),
            render.ScrollbarGeometry.ScrollOffset,
            PresenterScrollbarInteractionState.Default);

        Assert.NotNull(routed.Action);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionInspectorScrollOffset(routed.Action!.Id, out _, out _));
    }

    [Fact]
    public void MarkdownViewport_PartiallyVisibleParagraphRendersVisibleLines()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId, bodyScrollOffset: 260);
        OblivionCompactCardView view = GetExpandedBuiltCard().CompactView;
        OblivionExpandedBodyViewport viewport = Assert.IsType<OblivionExpandedBodyViewport>(
            OblivionCardRenderer.DescribeExpandedBodyViewport(page.Frame.Resolved, view, ExpandedDocCardId)!);
        DrawTextCommand[] commands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(commands);
        Assert.Contains(commands, command => command.Rect.Y < viewport.Bounds.Y + 4);
    }

    [Fact]
    public void MarkdownViewport_FullyOutsideBlockIsSkipped()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId, bodyScrollOffset: 900);
        DrawTextCommand[] commands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-0", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(commands);
    }

    [Fact]
    public void MarkdownViewport_MidParagraphScrollDoesNotBlankViewport()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId, bodyScrollOffset: 257);
        DrawTextCommand[] commands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(commands);
    }

    [Fact]
    public void ExpandedMarkdownBody_ClipsContentToViewport()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId);

        Assert.Contains(
            page.Frame.RenderCommands.OfType<PushClipCommand>(),
            command => command.Id.Contains($"{ExpandedDocCardId}.expanded-body-viewport", StringComparison.Ordinal));
    }

    [Fact]
    public void InspectorRawSource_ClipsContentToViewport()
    {
        PresenterPageRenderResult page = RenderDocsPage(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240);

        Assert.Contains(
            page.Frame.RenderCommands.OfType<PushClipCommand>(),
            command => command.Id.Contains(".wide-inspector-raw-source.source-frame", StringComparison.Ordinal));
    }

    [Fact]
    public void M15eManifest_RecordsIndependentScrollPanes()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"m15e-manifest-{Guid.NewGuid():N}");
        try
        {
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WriteIndependentScrollPanesManifest(
                outputDirectory,
                CreateDocsState(expandedCardId: ExpandedDocCardId));

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            Assert.True(document.RootElement.GetProperty("independentMainAndInspectorScroll").GetBoolean());
            Assert.True(document.RootElement.GetProperty("partialBlockRenderingImplemented").GetBoolean());
            Assert.True(File.Exists(textPath));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static PresenterNavigationState CreateDocsState(
        string selectedCardId = ExpandedDocCardId,
        string? expandedCardId = null,
        double bodyScrollOffset = 0,
        double mainScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs")
            .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, selectedCardId)
            .WithScrollOffset(OblivionWorkbenchCatalog.DocsPageId, mainScrollOffset)
            .WithInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId, inspectorScrollOffset)
            .WithRawMarkdownSourceScrollOffset(selectedCardId, rawSourceScrollOffset);

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state.WithCardViewState(
                OblivionWorkbenchCatalog.DocsPageId,
                expandedCardId,
                new OblivionCardViewState(true, bodyScrollOffset));
        }

        return state;
    }

    private static PresenterNavigationShellRenderResult RenderShell(PresenterNavigationState state)
    {
        return RenderShell(state, 1280, 720);
    }

    private static PresenterNavigationShellRenderResult RenderShell(PresenterNavigationState state, int width, int height)
    {
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            ProofOptions,
            layout);
    }

    private static PresenterPageRenderResult RenderDocsPage(
        string? expandedCardId = null,
        double bodyScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0)
    {
        PresenterNavigationState state = CreateDocsState(
            expandedCardId: expandedCardId,
            bodyScrollOffset: bodyScrollOffset,
            inspectorScrollOffset: inspectorScrollOffset,
            rawSourceScrollOffset: rawSourceScrollOffset);
        int width = 1280;
        int height = 720;
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            DemoState.Default,
            StandardTheme.Default,
            ProofOptions,
            layout.ContentVisibleWidth,
            layout.ViewportHeight,
            state,
            shellMode);
    }

    private static IReadOnlyList<OblivionCard> GetDocsCards()
    {
        return OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId, ProofOptions);
    }

    private static OblivionBuiltCard GetExpandedBuiltCard()
    {
        return Assert.Single(
            OblivionWorkbenchCatalog.GetBuiltPageCardsForSelection(
                OblivionWorkbenchCatalog.DocsPageId,
                ProofOptions,
                OblivionCardEffectState.Empty,
                CreateDocsState(expandedCardId: ExpandedDocCardId)),
            card => string.Equals(card.SourceCard.Id.Value, ExpandedDocCardId, StringComparison.Ordinal));
    }

    private static PresenterNavigationState Dispatch(PresenterNavigationState state, UiActionId actionId)
    {
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(1280, 720, PresenterShellMode.Wide);
        return PresenterNavigationDispatch.Dispatch(
            state,
            actionId,
            Model,
            ProofOptions,
            layout);
    }

    private static PresenterInputEvent Wheel(PresenterInputPoint point, float deltaY)
    {
        return new PresenterInputEvent(PresenterInputKind.Wheel, point, WheelDeltaY: deltaY);
    }

    private static PresenterInputEvent PointerPress(PresenterInputPoint point)
    {
        return new PresenterInputEvent(PresenterInputKind.PointerPressed, point, PresenterInputButton.Primary);
    }

    private static PresenterInputEvent PointerMove(PresenterInputPoint point)
    {
        return new PresenterInputEvent(PresenterInputKind.PointerMoved, point);
    }

    private static PresenterInputEvent PointerRelease(PresenterInputPoint point)
    {
        return new PresenterInputEvent(PresenterInputKind.PointerReleased, point, PresenterInputButton.Primary);
    }

    private static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static PresenterInputPoint OffsetPoint(PresenterInputPoint point, float dx, float dy)
    {
        return new PresenterInputPoint(point.X + dx, point.Y + dy);
    }
}
