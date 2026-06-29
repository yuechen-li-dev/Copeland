using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterStabilizationM10dTests
{
    [Fact]
    public void Presenter_TextDirectOutlinePage_DoesNotThrow()
    {
        Exception? error = Record.Exception(() => RenderPage(
            "text.direct-outline",
            new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true)));

        Assert.Null(error);
    }

    [Fact]
    public void Presenter_TextProofsPage_DoesNotThrow()
    {
        Exception? error = Record.Exception(() => RenderPage(
            "text.proofs",
            new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true)));

        Assert.Null(error);
    }

    [Fact]
    public void ExportPresenter_TextDirectOutlinePage_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "text-direct-outline.png"),
                new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "text",
                    SelectedTabId: "direct-outline"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("text.direct-outline", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_TextProofsPage_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "text-proofs.png"),
                new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "text",
                    SelectedTabId: "proofs"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("text.proofs", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void PresenterCards_HaveFiniteOuterBounds()
    {
        PresenterPageRenderResult page = RenderPage(
            "text.direct-outline",
            new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true));

        foreach (string cardId in new[]
                 {
                     "text-direct-outline-intro-card",
                     "direct-outline-proof-card",
                 })
        {
            PresenterCardFrame frame = PresenterCard.DescribeFrame(page.Frame.Resolved, cardId);
            AssertFinite(frame.Bounds);
        }
    }

    [Fact]
    public void PresenterCards_HaveFiniteContentBounds()
    {
        PresenterPageRenderResult page = RenderPage(
            "text.proofs",
            new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true));

        foreach (string cardId in new[]
                 {
                     "text-proofs-overview-card",
                     "text-proofs-status-card",
                 })
        {
            PresenterCardFrame frame = PresenterCard.DescribeFrame(page.Frame.Resolved, cardId);
            AssertFinite(frame.ContentBounds);
        }
    }

    [Fact]
    public void PresenterCards_DoNotAllowContentBleedOutsideCard()
    {
        PresenterPageRenderResult page = RenderPage(
            "text.current",
            new PresenterProofOptions());

        foreach (string cardId in new[]
                 {
                     "text-bitmap-current-card",
                     "text-bitmap-current-status-card",
                 })
        {
            PresenterCardFrame frame = PresenterCard.DescribeFrame(page.Frame.Resolved, cardId);
            IReadOnlyList<DrawTextCommand> commands = page.Frame.RenderCommands
                .OfType<DrawTextCommand>()
                .Where(command =>
                    command.Id.Contains(cardId, StringComparison.Ordinal) &&
                    command.Id.Contains(".body-line-", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(commands);
            Assert.All(commands, command => AssertRectInside(command.Rect, frame.ContentBounds, command.Id));
        }
    }

    [Fact]
    public void PresenterCards_ClipOversizedContentWhenConfigured()
    {
        IReadOnlyList<string> clipped = PresenterCard.ClipBodyLinesToFit(
            [
                "A very long presenter sample line that must be clipped before it escapes a narrow card body region.",
                "Second line should not survive when height only allows one line.",
            ],
            width: 150,
            height: 16,
            options: new PresenterCardOptions(Width: 240, Height: 120, ClipContent: true),
            color: StandardTheme.Default.Colors.MutedForeground);

        string onlyLine = Assert.Single(clipped);
        Assert.EndsWith("...", onlyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterPages_DoNotCreateNegativeRemainingStackSpace()
    {
        string[] pageIds =
        [
            "components.controls",
            "text.current",
            "text.direct-outline",
            "text.proofs",
            "diagnostics.layout",
            "legacy.m1e-card",
        ];

        foreach (string pageId in pageIds)
        {
            Exception? error = Record.Exception(() => RenderPage(
                pageId,
                new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true)));
            Assert.Null(error);
        }
    }

    [Fact]
    public void NavigationInput_ClickScrollbarTrack_PagesScroll()
    {
        PresenterNavigationState state = ScrolledPageState(pageOffset: 0);
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            PointerPress(BelowThumb(render.ScrollbarGeometry)));

        Assert.True(next.GetScrollOffset("components.controls") > 0);
    }

    [Fact]
    public void NavigationInput_ClickScrollbarTrack_ClampsOffset()
    {
        PresenterNavigationState state = ScrolledPageState(pageOffset: 0);
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = render.NavigationState;
        for (int index = 0; index < 20; index++)
        {
            next = DispatchInput(next, PointerPress(BelowThumb(RenderShell(next).ScrollbarGeometry)));
        }

        double expected = PresenterScrollRegion.ComputeMaxScrollOffset(
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", ProofOptions),
            PresenterNavigationLayout.Default.ViewportHeight);

        Assert.Equal(expected, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void NavigationInput_DragScrollbarThumb_UpdatesScrollOffset()
    {
        PresenterNavigationState state = ScrolledPageState(pageOffset: 120);
        PresenterNavigationState next = DispatchSequence(
            state,
            [
                PointerPress(Center(RenderShell(state).ScrollbarGeometry.ThumbRect)),
                PointerMove(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 80)),
                PointerRelease(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 80)),
            ]);

        Assert.True(next.GetScrollOffset("components.controls") > 120);
    }

    [Fact]
    public void NavigationInput_DragScrollbarThumb_ClampsOffset()
    {
        PresenterNavigationState state = ScrolledPageState(pageOffset: 120);
        PresenterNavigationState next = DispatchSequence(
            state,
            [
                PointerPress(Center(RenderShell(state).ScrollbarGeometry.ThumbRect)),
                PointerMove(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 800)),
                PointerRelease(OffsetPoint(Center(RenderShell(state).ScrollbarGeometry.ThumbRect), 0, 800)),
            ]);

        double expected = PresenterScrollRegion.ComputeMaxScrollOffset(
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", ProofOptions),
            PresenterNavigationLayout.Default.ViewportHeight);

        Assert.Equal(expected, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void NavigationInput_WheelScrollStillWorks()
    {
        PresenterNavigationState state = ScrolledPageState(pageOffset: 0);
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), -1));

        Assert.Equal(PresenterNavigationInputRouter.ScrollWheelMultiplier, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void Presenter_DefaultRun_StillUsesNavigationShell()
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse([]);

        Assert.True(options.NavigationOptions.IncludeNavigationShell);
    }

    [Fact]
    public void Presenter_SidebarClickStillWorks()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            item => item.SectionId == "components");

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal("components", next.SelectedSectionId);
    }

    [Fact]
    public void Presenter_TabClickStillWorks()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            item => item.TabId == "status");

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal("status", next.GetSelectedTabId("overview", Model));
    }

    [Fact]
    public void Presenter_PerPageScrollOffsetsStillPreserved()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", 144)
            .WithSelectedTab("components", "cards");

        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            item => item.TabId == "controls");

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal(144, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void Presenter_LegacyM1eCardStillAvailable()
    {
        PresenterNavigationSection legacy = Assert.Single(Model.Sections, section => section.Id == "legacy");
        PresenterNavigationTab tab = Assert.Single(legacy.Tabs);

        Assert.Equal("m1e-card", tab.Id);
        Assert.Equal("legacy.m1e-card", tab.PageId);
    }

    private static PresenterNavigationModel Model => PresenterNavigationCatalog.CreateModel();

    private static PresenterProofOptions ProofOptions => new();

    private static PresenterPageRenderResult RenderPage(string pageId, PresenterProofOptions proofOptions)
    {
        return PresenterNavigationCatalog.RenderPage(
            pageId,
            DemoState.Default,
            StandardTheme.Default,
            proofOptions,
            PresenterNavigationLayout.Default.ContentVisibleWidth);
    }

    private static PresenterNavigationShellRenderResult RenderShell(PresenterNavigationState? state = null)
    {
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state ?? PresenterNavigationState.CreateDefault(Model),
            StandardTheme.Default,
            ProofOptions);
    }

    private static PresenterNavigationState ScrolledPageState(double pageOffset)
    {
        return PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", pageOffset);
    }

    private static PresenterNavigationState DispatchInput(
        PresenterNavigationState state,
        PresenterInputEvent inputEvent)
    {
        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, inputEvent);
        if (routed.ActionId is null)
        {
            return render.NavigationState;
        }

        return PresenterNavigationDispatch.Dispatch(
            render.NavigationState,
            routed.ActionId.Value,
            Model,
            ProofOptions,
            PresenterNavigationLayout.Default);
    }

    private static PresenterNavigationState DispatchSequence(
        PresenterNavigationState initialState,
        IReadOnlyList<PresenterInputEvent> inputs)
    {
        PresenterNavigationState state = initialState;
        PresenterScrollbarInteractionState interactionState = PresenterScrollbarInteractionState.Default;

        foreach (PresenterInputEvent input in inputs)
        {
            PresenterNavigationShellRenderResult render = RenderShell(state);
            PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, input, interactionState);
            interactionState = routed.InteractionState;

            if (routed.ActionId is not null)
            {
                state = PresenterNavigationDispatch.Dispatch(
                    render.NavigationState,
                    routed.ActionId.Value,
                    Model,
                    ProofOptions,
                    PresenterNavigationLayout.Default);
            }
        }

        return state;
    }

    private static PresenterInputEvent PointerPress(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerPressed,
            point,
            PresenterInputButton.Primary,
            BackendName: "Test");
    }

    private static PresenterInputEvent PointerMove(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerMoved,
            point,
            PresenterInputButton.Primary,
            BackendName: "Test");
    }

    private static PresenterInputEvent PointerRelease(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerReleased,
            point,
            PresenterInputButton.Primary,
            BackendName: "Test");
    }

    private static PresenterInputEvent Wheel(PresenterInputPoint point, float deltaY)
    {
        return new PresenterInputEvent(
            PresenterInputKind.Wheel,
            point,
            PresenterInputButton.None,
            deltaY,
            "Test");
    }

    private static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static PresenterInputPoint BelowThumb(ScrollbarGeometry geometry)
    {
        return new PresenterInputPoint(
            (float)(geometry.TrackRect.X + (geometry.TrackRect.Width / 2)),
            (float)Math.Min(
                geometry.TrackRect.Y + geometry.TrackRect.Height - 1,
                geometry.ThumbRect.Y + geometry.ThumbRect.Height + 24));
    }

    private static PresenterInputPoint OffsetPoint(PresenterInputPoint point, float deltaX, float deltaY)
    {
        return new PresenterInputPoint(point.X + deltaX, point.Y + deltaY);
    }

    private static void AssertFinite(Rect rect)
    {
        Assert.True(double.IsFinite(rect.X));
        Assert.True(double.IsFinite(rect.Y));
        Assert.True(double.IsFinite(rect.Width));
        Assert.True(double.IsFinite(rect.Height));
        Assert.True(rect.Width >= 0);
        Assert.True(rect.Height >= 0);
    }

    private static void AssertRectInside(Rect inner, Rect outer, string id)
    {
        Assert.True(inner.X >= outer.X, $"{id} left outside");
        Assert.True(inner.Y >= outer.Y, $"{id} top outside");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{id} right outside");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{id} bottom outside");
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m10d-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
