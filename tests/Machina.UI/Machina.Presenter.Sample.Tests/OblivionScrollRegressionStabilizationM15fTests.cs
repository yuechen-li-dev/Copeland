using System.Text.Json;
using Machina.Core.Actions;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionScrollRegressionStabilizationM15fTests
{
    private const string ExpandedDocCardId = "doc-aurelian-build-topology-m13b";
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();

    [Fact]
    public void MainCardStack_WheelOverCards_UpdatesMainScrollOffset()
    {
        PresenterNavigationState state = CreateDocsState();
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget mainStack = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack);

        PresenterNavigationState next = DispatchInput(
            state,
            render,
            Wheel(ToRootPoint(render, mainStack.Bounds), -1));

        Assert.True(next.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId) > 0);
    }

    [Fact]
    public void MainCardStack_WheelOverCards_DoesNotUpdateInspectorScrollOffset()
    {
        PresenterNavigationState state = CreateDocsState(inspectorScrollOffset: 180);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget mainStack = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack);

        PresenterNavigationState next = DispatchInput(
            state,
            render,
            Wheel(ToRootPoint(render, mainStack.Bounds), -1));

        Assert.Equal(180, next.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void MainCardStack_ScrollbarThumbDrag_UpdatesMainScrollOffset()
    {
        PresenterNavigationState state = CreateDocsState(mainScrollOffset: 180);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget mainStack = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack);

        PresenterNavigationState next = DispatchSequence(
            state,
            [
                PointerPress(ToRootPoint(render, mainStack.ScrollbarGeometry.ThumbRect)),
                PointerMove(OffsetPoint(ToRootPoint(render, mainStack.ScrollbarGeometry.ThumbRect), 0, 60)),
                PointerRelease(OffsetPoint(ToRootPoint(render, mainStack.ScrollbarGeometry.ThumbRect), 0, 60)),
            ]);

        Assert.True(next.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId) > 180);
    }

    [Fact]
    public void MainCardStack_ScrollbarDrag_DoesNotToggleExpansion()
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: ExpandedDocCardId, mainScrollOffset: 180);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget mainStack = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack);

        PresenterNavigationState next = DispatchSequence(
            state,
            [
                PointerPress(ToRootPoint(render, mainStack.ScrollbarGeometry.ThumbRect)),
                PointerMove(OffsetPoint(ToRootPoint(render, mainStack.ScrollbarGeometry.ThumbRect), 0, 50)),
                PointerRelease(OffsetPoint(ToRootPoint(render, mainStack.ScrollbarGeometry.ThumbRect), 0, 50)),
            ]);

        Assert.True(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
    }

    [Fact]
    public void MainCardStack_ScrollOffsetClamps()
    {
        PresenterNavigationState top = Dispatch(
            CreateDocsState(),
            PresenterNavigationActions.SetOblivionMainCardStackScrollOffset(OblivionWorkbenchCatalog.DocsPageId, -500));
        PresenterNavigationState bottom = Dispatch(
            CreateDocsState(),
            PresenterNavigationActions.SetOblivionMainCardStackScrollOffset(OblivionWorkbenchCatalog.DocsPageId, 10_000));
        double expectedBottom = OblivionWorkbenchCatalog.ClampMainCardStackScrollOffset(
            OblivionWorkbenchCatalog.DocsPageId,
            10_000,
            ProofOptions,
            CreateDocsState(),
            CreateLayout());

        Assert.Equal(0, top.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
        Assert.Equal(expectedBottom, bottom.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void MainCardStack_HitTestUsesEffectivePresenterSurfaceCoordinates()
    {
        PresenterNavigationState state = CreateDocsState();
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget mainStack = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack);

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            Wheel(ToRootPoint(render, mainStack.Bounds), -1));

        UiActionId actionId = Assert.IsType<UiActionId>(routed.ActionId);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionMainCardStackScrollOffset(actionId, out _, out _));
    }

    [Fact]
    public void MainCardStack_RegionIsNotShadowedByInspectorRegion()
    {
        PresenterNavigationState state = CreateDocsState();
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget mainStack = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack);

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            Wheel(ToRootPoint(render, mainStack.Bounds), -1));

        UiActionId actionId = Assert.IsType<UiActionId>(routed.ActionId);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionMainCardStackScrollOffset(actionId, out _, out _));
        Assert.False(PresenterNavigationActions.TryParseSetOblivionInspectorScrollOffset(actionId, out _, out _));
    }

    [Fact]
    public void MainCardStack_WideModeWheelDoesNotDispatchPageScrollAction()
    {
        PresenterNavigationState state = CreateDocsState();
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget mainStack = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack);

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            Wheel(ToRootPoint(render, mainStack.Bounds), -1));

        UiActionId actionId = Assert.IsType<UiActionId>(routed.ActionId);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionMainCardStackScrollOffset(actionId, out _, out _));
        Assert.False(PresenterNavigationActions.TryParseSetScrollOffset(actionId, out _, out _));
    }

    [Fact]
    public void InspectorScroll_UpdatesInspectorOffsetOnly()
    {
        PresenterNavigationState state = CreateDocsState(mainScrollOffset: 220);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget inspector = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionInspectorPane);

        PresenterNavigationState next = DispatchInput(
            state,
            render,
            Wheel(ToRootPoint(render, inspector.Bounds), -1));

        Assert.Equal(220, next.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
        Assert.True(next.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId) > 0);
    }

    [Fact]
    public void InspectorScroll_DoesNotUpdateMainStackOffset()
    {
        PresenterNavigationState state = CreateDocsState(mainScrollOffset: 180);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget inspector = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionInspectorPane);

        PresenterNavigationState next = DispatchInput(
            state,
            render,
            Wheel(ToRootPoint(render, inspector.Bounds), -1));

        Assert.Equal(180, next.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void InspectorScroll_DoesNotResetSelectedCard()
    {
        PresenterNavigationState state = CreateDocsState(selectedCardId: ExpandedDocCardId);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget inspector = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionInspectorPane);

        PresenterNavigationState next = DispatchInput(
            state,
            render,
            Wheel(ToRootPoint(render, inspector.Bounds), -1));

        Assert.Equal(ExpandedDocCardId, next.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, GetDocsCards()));
    }

    [Fact]
    public void InspectorScroll_WheelRoutesToSingleRegion()
    {
        PresenterNavigationState state = CreateDocsState();
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget inspector = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionInspectorPane);

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            Wheel(ToRootPoint(render, inspector.Bounds), -1));

        UiActionId actionId = Assert.IsType<UiActionId>(routed.ActionId);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionInspectorScrollOffset(actionId, out _, out _));
        Assert.False(PresenterNavigationActions.TryParseSetOblivionMainCardStackScrollOffset(actionId, out _, out _));
    }

    [Fact]
    public void InspectorScroll_RawSourceLayoutIsCachedAcrossScrollTicks()
    {
        OblivionMarkdownRenderer.ResetDiagnostics();
        var session = new PresenterNavigationRenderSession();
        OblivionCardBody body = GetDocsCards().First(card => string.Equals(card.Id.Value, ExpandedDocCardId, StringComparison.Ordinal)).Body;

        _ = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240), session: session);
        int buildCountAfterInitialRender = OblivionMarkdownRenderer.GetRawMarkdownSourceLayoutBuildCountForBody(body);
        PresenterNavigationShellRenderResult scrolled = RenderShell(
            CreateDocsState(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 320),
            session: session);
        int buildCountAfterScrolledRender = OblivionMarkdownRenderer.GetRawMarkdownSourceLayoutBuildCountForBody(body);

        Assert.Equal(2, scrolled.Diagnostics.PageRenderCount);
        Assert.True(buildCountAfterInitialRender >= 1);
        Assert.Equal(buildCountAfterInitialRender, buildCountAfterScrolledRender);
    }

    [Fact]
    public void M15f_PreservesIndependentScrollPanes()
    {
        PresenterNavigationState state = CreateDocsState(mainScrollOffset: 160, inspectorScrollOffset: 220);

        Assert.Equal(160, state.GetScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
        Assert.Equal(220, state.GetInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId));
    }

    [Fact]
    public void M15f_PreservesExpandedBodyScroll()
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: ExpandedDocCardId);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget body = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody, ExpandedDocCardId);

        OblivionPageInteractionRoutingResult routed = render.PageRender!.OblivionInteraction!.RouteInput(
            Wheel(Center(body.Bounds), -1),
            render.ScrollbarGeometry.ScrollOffset,
            PresenterScrollbarInteractionState.Default);

        Assert.NotNull(routed.Action);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionCardBodyScrollOffset(routed.Action!.Id, out _, out _, out double offset));
        Assert.True(offset > 0);
    }

    [Fact]
    public void M15f_PreservesRawSourceScroll()
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240);
        PresenterNavigationShellRenderResult render = RenderShell(state);
        OblivionScrollRegionTarget rawSource = GetScrollRegion(render, PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource, ExpandedDocCardId);

        OblivionPageInteractionRoutingResult routed = render.PageRender!.OblivionInteraction!.RouteInput(
            Wheel(Center(rawSource.Bounds), -1),
            render.ScrollbarGeometry.ScrollOffset,
            PresenterScrollbarInteractionState.Default);

        Assert.NotNull(routed.Action);
        Assert.True(PresenterNavigationActions.TryParseSetOblivionRawMarkdownSourceScrollOffset(routed.Action!.Id, out _, out _, out double offset));
        Assert.True(offset > 0);
    }

    [Fact]
    public void M15f_PreservesPartialViewportCulling()
    {
        PresenterPageRenderResult page = RenderDocsPage(bodyScrollOffset: 257);
        DrawTextCommand[] commands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(commands);
    }

    [Fact]
    public void M15f_PreservesM15bResizeBehavior()
    {
        PresenterNavigationShellRenderResult wide = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), width: 1280, height: 720);
        PresenterNavigationShellRenderResult compact = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), width: 960, height: 540);

        Assert.Equal(PresenterShellMode.Wide, wide.ShellMode);
        Assert.Equal(PresenterShellMode.Compact, compact.ShellMode);
    }

    [Fact]
    public void M15f_PreservesM15dReadingStyle()
    {
        OblivionMarkdownReadingStyle style = OblivionCardRenderer.MarkdownReadingStyle;

        Assert.NotEqual(style.Surface, style.Foreground);
        Assert.NotEqual(style.SourceSurface, style.SourceForeground);
    }

    [Fact]
    public void M15f_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = CreateM15fManifestDocument();

        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M15f_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = CreateM15fManifestDocument();

        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M15f_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = CreateM15fManifestDocument();

        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M15f_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = CreateM15fManifestDocument();

        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M15fManifest_RecordsRegressionStabilization()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"m15f-manifest-{Guid.NewGuid():N}");
        try
        {
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WriteScrollRegressionStabilizationManifest(
                outputDirectory,
                CreateDocsState(expandedCardId: ExpandedDocCardId),
                inspectorLagFixed: true,
                inspectorLagRootCauseDocumented: true,
                inspectorLagBlockerDocumented: false);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            Assert.True(document.RootElement.GetProperty("mainCardStackWheelFixed").GetBoolean());
            Assert.True(document.RootElement.GetProperty("mainCardStackScrollbarDragFixed").GetBoolean());
            Assert.True(document.RootElement.GetProperty("inspectorLagInvestigated").GetBoolean());
            Assert.True(document.RootElement.GetProperty("inspectorLagFixed").GetBoolean());
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

    private static PresenterNavigationState Dispatch(PresenterNavigationState state, UiActionId actionId)
    {
        return PresenterNavigationDispatch.Dispatch(
            state,
            actionId,
            Model,
            ProofOptions,
            CreateLayout());
    }

    private static PresenterNavigationState DispatchInput(
        PresenterNavigationState state,
        PresenterNavigationShellRenderResult render,
        PresenterInputEvent inputEvent)
    {
        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, inputEvent);
        return routed.ActionId is null ? state : Dispatch(state, routed.ActionId.Value);
    }

    private static PresenterNavigationState DispatchSequence(
        PresenterNavigationState state,
        IReadOnlyList<PresenterInputEvent> events)
    {
        PresenterScrollbarInteractionState interactionState = PresenterScrollbarInteractionState.Default;
        PresenterNavigationState currentState = state;

        foreach (PresenterInputEvent inputEvent in events)
        {
            PresenterNavigationShellRenderResult render = RenderShell(currentState);
            PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, inputEvent, interactionState);
            interactionState = routed.InteractionState;
            if (routed.ActionId is not null)
            {
                currentState = Dispatch(currentState, routed.ActionId.Value);
            }
        }

        return currentState;
    }

    private static PresenterNavigationShellRenderResult RenderShell(
        PresenterNavigationState state,
        int width = 1280,
        int height = 720,
        PresenterNavigationRenderSession? session = null)
    {
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            ProofOptions,
            session,
            layout);
    }

    private static PresenterPageRenderResult RenderDocsPage(double bodyScrollOffset)
    {
        PresenterNavigationState state = CreateDocsState(
            expandedCardId: ExpandedDocCardId,
            bodyScrollOffset: bodyScrollOffset);
        PresenterNavigationLayout layout = CreateLayout();
        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            DemoState.Default,
            StandardTheme.Default,
            ProofOptions,
            layout.ContentVisibleWidth,
            layout.ViewportHeight,
            state,
            layout.ShellMode);
    }

    private static PresenterNavigationLayout CreateLayout()
    {
        return PresenterNavigationLayout.Create(1280, 720, PresenterShellMode.Wide);
    }

    private static IReadOnlyList<OblivionCard> GetDocsCards()
    {
        return OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId, ProofOptions);
    }

    private static OblivionScrollRegionTarget GetScrollRegion(
        PresenterNavigationShellRenderResult render,
        PresenterScrollbarTargetKind kind,
        string? cardId = null)
    {
        return Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            target => target.Target.Kind == kind &&
                (cardId is null || string.Equals(target.Target.CardId, cardId, StringComparison.Ordinal)));
    }

    private static PresenterInputPoint ToRootPoint(PresenterNavigationShellRenderResult render, Rect localRect)
    {
        return new PresenterInputPoint(
            (float)(render.ChromeGeometry.ContentViewportRect.X + localRect.X + (localRect.Width / 2)),
            (float)(render.ChromeGeometry.ContentViewportRect.Y + localRect.Y + (localRect.Height / 2)));
    }

    private static PresenterInputPoint OffsetPoint(PresenterInputPoint point, float dx, float dy)
    {
        return new PresenterInputPoint(point.X + dx, point.Y + dy);
    }

    private static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static PresenterInputEvent Wheel(PresenterInputPoint point, float deltaY)
    {
        return new PresenterInputEvent(PresenterInputKind.Wheel, point, WheelDeltaY: deltaY);
    }

    private static PresenterInputEvent PointerPress(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerPressed,
            point,
            Button: PresenterInputButton.Primary);
    }

    private static PresenterInputEvent PointerMove(PresenterInputPoint point)
    {
        return new PresenterInputEvent(PresenterInputKind.PointerMoved, point);
    }

    private static PresenterInputEvent PointerRelease(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerReleased,
            point,
            Button: PresenterInputButton.Primary);
    }

    private static JsonDocument CreateM15fManifestDocument()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"m15f-boundary-{Guid.NewGuid():N}");
        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WriteScrollRegressionStabilizationManifest(
                outputDirectory,
                CreateDocsState(expandedCardId: ExpandedDocCardId),
                inspectorLagFixed: true,
                inspectorLagRootCauseDocumented: true,
                inspectorLagBlockerDocumented: false);
            string json = File.ReadAllText(jsonPath);
            return JsonDocument.Parse(json);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}
