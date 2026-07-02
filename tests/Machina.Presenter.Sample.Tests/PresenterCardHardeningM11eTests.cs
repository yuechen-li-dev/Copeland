using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Renderer.Raster.Colors;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterCardHardeningM11eTests
{
    private static readonly StandardTheme Theme = StandardTheme.Default;

    [Fact]
    public void PresenterTextCard_BodyHeight_UsesInnerHeightMinusBodyTop()
    {
        PresenterCardOptions options = new(Width: 420, Height: 236);
        PresenterCardLayout layout = PresenterCard.ComputeTextLayout(options, Theme.Card.Default, badgeCount: 2);
        double expectedInnerHeight = options.Height - (Theme.Card.Default.ContentInset * 2);

        Assert.Equal(expectedInnerHeight - layout.BodyTop, layout.BodyHeight, 6);
    }

    [Fact]
    public void PresenterTextCard_BodyWidth_DoesNotDoubleSubtractInset()
    {
        PresenterCardOptions options = new(Width: 420, Height: 236);
        PresenterCardLayout layout = PresenterCard.ComputeTextLayout(options, Theme.Card.Default, badgeCount: 0);
        double expectedInnerWidth = options.Width - (Theme.Card.Default.ContentInset * 2);

        Assert.Equal(expectedInnerWidth, layout.BodyWidth, 6);
    }

    [Fact]
    public void PresenterTextCard_ShowsMoreThanThreeBodyLinesWhenSpaceAllows()
    {
        PresenterCardOptions options = new(Width: 420, Height: 260);
        PresenterCardLayout layout = PresenterCard.ComputeTextLayout(options, Theme.Card.Default, badgeCount: 1);
        IReadOnlyList<string> visibleLines = PresenterCard.ClipBodyLinesToFit(
            CreateLines(6),
            layout.BodyWidth,
            layout.BodyHeight,
            options,
            Theme.Colors.MutedForeground);

        Assert.True(visibleLines.Count > 3);
    }

    [Fact]
    public void PresenterTextCard_BulletPrefix_DoesNotClipFirstContentCharacters()
    {
        PresenterCardOptions options = new(Width: 340, Height: 220);
        PresenterCardLayout layout = PresenterCard.ComputeTextLayout(options, Theme.Card.Default, badgeCount: 0);
        string visibleLine = Assert.Single(PresenterCard.ClipBodyLinesToFit(
            ["Leading content should remain visible after the bullet prefix is reserved separately."],
            layout.BodyWidth,
            options.BodyLineHeight,
            options,
            Theme.Colors.MutedForeground));

        Assert.StartsWith("\u2022 L", visibleLine, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterTextCard_BodyLines_AreClippedOnlyWhenCapacityExceeded()
    {
        PresenterCardOptions options = new(Width: 380, Height: 220);
        PresenterCardLayout layout = PresenterCard.ComputeTextLayout(options, Theme.Card.Default, badgeCount: 0);

        IReadOnlyList<string> withinCapacity = PresenterCard.ClipBodyLinesToFit(
            ["alpha", "beta"],
            layout.BodyWidth,
            layout.BodyHeight,
            options,
            Theme.Colors.MutedForeground);
        IReadOnlyList<string> exceeded = PresenterCard.ClipBodyLinesToFit(
            CreateLines(12),
            140,
            options.BodyLineHeight,
            options,
            Theme.Colors.MutedForeground);

        Assert.Equal(["\u2022 alpha", "\u2022 beta"], withinCapacity);
        Assert.Single(exceeded);
        Assert.EndsWith("...", exceeded[0], StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterCardLayout_ComputesOuterContentHeaderBodyRects()
    {
        PresenterCardLayout layout = PresenterCardLayoutHelper.ComputeLayout(
            width: 420,
            height: 236,
            contentInset: 16,
            bodyTopInContent: 84);

        Assert.Equal(new Rect(0, 0, 420, 236), layout.OuterRect);
        Assert.Equal(new Rect(16, 16, 388, 204), layout.ContentRectInOuter);
        Assert.Equal(new Rect(0, 0, 388, 84), layout.HeaderRectInContent);
        Assert.Equal(new Rect(0, 84, 388, 120), layout.BodyRectInContent);
    }

    [Fact]
    public void PresenterCardLayout_BodyTopIsContentLocal()
    {
        PresenterCardLayout layout = PresenterCardLayoutHelper.ComputeLayout(
            width: 420,
            height: 236,
            contentInset: 16,
            bodyTopInContent: 84);

        Assert.Equal(84, layout.BodyTop, 6);
        Assert.NotEqual(layout.ContentRectInOuter.Y + 84, layout.BodyTop);
    }

    [Fact]
    public void PresenterCardLayout_DoesNotMixOuterAndInnerCoordinates()
    {
        PresenterCardLayout layout = PresenterCardLayoutHelper.ComputeLayout(
            width: 420,
            height: 236,
            contentInset: 16,
            bodyTopInContent: 84);

        Assert.Equal(layout.InnerHeight, layout.BodyRectInContent.Y + layout.BodyRectInContent.Height, 6);
        Assert.NotEqual(layout.OuterRect.Height, layout.BodyRectInContent.Y + layout.BodyRectInContent.Height);
    }

    [Fact]
    public void PresenterCardBuilders_UseSharedCardLayoutHelper()
    {
        PresenterCardOptions presenterOptions = new(Width: 420, Height: 236);
        PresenterCardLayout presenterLayout = PresenterCard.ComputeTextLayout(presenterOptions, Theme.Card.Default, badgeCount: 2);
        PresenterCardLayout expectedPresenterLayout = PresenterCardLayoutHelper.ComputeLayout(
            presenterOptions.Width,
            presenterOptions.Height,
            Theme.Card.Default.ContentInset,
            bodyTopInContent: presenterLayout.BodyTop);

        OblivionCard card = GetPersistedCardsPage().Cards[0];
        OblivionCardRenderOptions oblivionOptions = new(Width: 420, Height: OblivionWorkbenchCatalog.GetCardHeight(card));
        double oblivionBodyTop = ComputeOblivionBodyTop(card, oblivionOptions);
        PresenterCardLayout oblivionLayout = OblivionCardRenderer.ComputeLayout(card, oblivionOptions, Theme.Card.Default, oblivionBodyTop);

        Assert.Equal(expectedPresenterLayout, presenterLayout);
        Assert.Equal(
            PresenterCardLayoutHelper.ComputeLayout(
                oblivionOptions.Width,
                oblivionOptions.Height,
                Theme.Card.Default.ContentInset,
                oblivionBodyTop,
                footerHeightInContent: ComputeOblivionFooterHeight(card, oblivionOptions)),
            oblivionLayout);
    }

    [Fact]
    public void HostedCard_DoesNotPaintFullWidthDarkBodyBackground()
    {
        PresenterPageRenderResult page = RenderPage("legacy.m1e-card");
        Rect bodyFrame = FindRectBySuffix(page.Frame.Resolved, "legacy-settings-wrapper-card.body-frame");
        Rect hostedSlot = FindRectBySuffix(page.Frame.Resolved, "legacy-settings-card-slot");

        int sampleX = (int)Math.Floor(hostedSlot.X + hostedSlot.Width + 40);
        int sampleY = (int)Math.Floor(bodyFrame.Y + 40);
        Rgba32 sample = page.Frame.RasterFrame.Surface.GetPixel(sampleX, sampleY);

        Assert.True(sampleX < bodyFrame.X + bodyFrame.Width);
        Assert.NotEqual(Rgba32.FromRgba(0x0B1220FF), sample);
    }

    [Fact]
    public void LegacyM1eCard_DoesNotShowDarkRectangleOutsideHostedContent()
    {
        PresenterPageRenderResult page = RenderPage("legacy.m1e-card");
        Rect bodyFrame = FindRectBySuffix(page.Frame.Resolved, "legacy-settings-wrapper-card.body-frame");
        Rect hostedSlot = FindRectBySuffix(page.Frame.Resolved, "legacy-settings-card-slot");

        Assert.True(hostedSlot.X + hostedSlot.Width < bodyFrame.X + bodyFrame.Width);

        int sampleX = (int)Math.Floor(hostedSlot.X + hostedSlot.Width + 24);
        int sampleY = (int)Math.Floor(bodyFrame.Y + (bodyFrame.Height / 2));
        Assert.NotEqual(Rgba32.FromRgba(0x0B1220FF), page.Frame.RasterFrame.Surface.GetPixel(sampleX, sampleY));
    }

    [Fact]
    public void LegacyM1eCard_RemainsAvailable()
    {
        PresenterNavigationSection legacy = Assert.Single(
            PresenterNavigationCatalog.CreateModel().Sections,
            section => section.Id == "legacy");

        Assert.Contains(legacy.Tabs, tab => tab.PageId == "legacy.m1e-card");
    }

    [Fact]
    public void OblivionCards_ShowExpectedBodyLineCapacity()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId);
        int bodyLineCount = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Count(command =>
                command.Id.Contains("oblivion-code-theory-card", StringComparison.Ordinal) &&
                command.Id.Contains(".body-line-", StringComparison.Ordinal));

        Assert.True(bodyLineCount > 3);
    }

    [Fact]
    public void OblivionCards_DoNotBleedBodyTextOutsideCard()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.DocsPageId);
        PresenterCardFrame frame = OblivionCardRenderer.DescribeFrame(page.Frame.Resolved, "doc-aurelian-build-topology-m13b");
        IReadOnlyList<DrawTextCommand> commands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command =>
                command.Id.Contains("doc-aurelian-build-topology-m13b", StringComparison.Ordinal) &&
                (command.Id.Contains(".body-line-", StringComparison.Ordinal) ||
                 command.Id.Contains(".summary", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(commands);
        Assert.All(commands, command => AssertRectInside(command.Rect, frame.ContentBounds, command.Id));
    }

    [Fact]
    public void OblivionPersistedCards_RenderWithSharedCardLayout()
    {
        OblivionCard card = GetPersistedCardsPage().Cards[0];
        OblivionCardRenderOptions options = new(Width: 420, Height: OblivionWorkbenchCatalog.GetCardHeight(card));
        double bodyTop = ComputeOblivionBodyTop(card, options);
        PresenterCardLayout layout = OblivionCardRenderer.ComputeLayout(card, options, Theme.Card.Default, bodyTop);

        Assert.Equal(
            PresenterCardLayoutHelper.ComputeLayout(
                options.Width,
                options.Height,
                Theme.Card.Default.ContentInset,
                bodyTop,
                ComputeOblivionFooterHeight(card, options)),
            layout);
    }

    [Fact]
    public void ScrollbarThumb_IsInsideTrackAtTop()
    {
        ScrollbarGeometry geometry = CreateScrollbarGeometry(scrollOffset: 0);
        AssertRectInside(geometry.ThumbRect, geometry.TrackRect, "thumb-top");
    }

    [Fact]
    public void ScrollbarThumb_IsInsideTrackAtMiddle()
    {
        ScrollbarGeometry geometry = CreateScrollbarGeometry(scrollOffset: 172);
        AssertRectInside(geometry.ThumbRect, geometry.TrackRect, "thumb-middle");
    }

    [Fact]
    public void ScrollbarThumb_IsInsideTrackAtBottom()
    {
        ScrollbarGeometry geometry = CreateScrollbarGeometry(scrollOffset: CreateScrollbarGeometry(0).MaxScrollOffset);
        AssertRectInside(geometry.ThumbRect, geometry.TrackRect, "thumb-bottom");
    }

    [Fact]
    public void ScrollbarTrack_IsInsideViewportChromeBounds()
    {
        PresenterNavigationLayout layout = PresenterNavigationLayout.Default;
        Rect track = layout.ScrollbarTrackRect;
        Rect viewport = new(layout.ViewportLeft, layout.ViewportTop, layout.ViewportWidth, layout.ViewportHeight);

        AssertRectInside(track, viewport, "scrollbar-track");
    }

    [Fact]
    public void ComponentsControlsPage_OverflowingContentShowsVisibleThumb()
    {
        PresenterNavigationShellRenderResult render = RenderShell(ScrolledControlsState(0));

        Assert.True(render.ScrollbarGeometry.IsVisible);
        Assert.True(render.ScrollbarGeometry.ThumbRect.Height > 0);
        AssertRectInside(render.ScrollbarGeometry.ThumbRect, render.ScrollbarGeometry.TrackRect, "controls-thumb");
    }

    [Fact]
    public void ShortPages_HideOrDisableScrollbarDeterministically()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(PresenterNavigationCatalog.CreateModel())
            .WithSelectedSection("overview")
            .WithSelectedTab("overview", "status");
        PresenterNavigationShellRenderResult render = RenderShell(state);

        Assert.False(render.ScrollbarGeometry.IsVisible);
        Assert.Equal(0, render.ScrollbarGeometry.ThumbRect.Width);
        Assert.Equal(0, render.ScrollbarGeometry.ThumbRect.Height);
    }

    [Fact]
    public void ExportPresenter_OblivionCards_ShowsFullerBodyText()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-card-hardening-oblivion-cards.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("oblivion.cards", result.NavigationPageId);
            Assert.True(CountBodyLines(RenderPage(OblivionWorkbenchCatalog.CardsPageId), "oblivion-code-theory-card") > 3);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_LegacyM1eCard_NoHostedBackgroundBleed()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-card-hardening-legacy-m1e-card.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "legacy",
                    SelectedTabId: "m1e-card"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("legacy.m1e-card", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_ComponentsControls_ScrollbarThumbVisible()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-card-hardening-components-controls.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "components",
                    SelectedTabId: "controls"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("components.controls", result.NavigationPageId);
            Assert.True(result.ScrollbarGeometry!.IsVisible);
            AssertRectInside(result.ScrollbarGeometry.ThumbRect, result.ScrollbarGeometry.TrackRect, "export-thumb");
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    private static PresenterPageRenderResult RenderPage(string pageId, PresenterProofOptions? proofOptions = null)
    {
        return PresenterNavigationCatalog.RenderPage(
            pageId,
            DemoState.Default,
            Theme,
            proofOptions ?? new PresenterProofOptions(),
            PresenterNavigationLayout.Default.ContentVisibleWidth);
    }

    private static PresenterNavigationShellRenderResult RenderShell(PresenterNavigationState state)
    {
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            Theme,
            new PresenterProofOptions());
    }

    private static PresenterNavigationState ScrolledControlsState(double offset)
    {
        return PresenterNavigationState.CreateDefault(PresenterNavigationCatalog.CreateModel())
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", offset);
    }

    private static ScrollbarGeometry CreateScrollbarGeometry(double scrollOffset)
    {
        return PresenterScrollRegion.ComputeScrollbarGeometry(
            PresenterNavigationLayout.Default.ScrollbarTrackRect,
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", new PresenterProofOptions()),
            PresenterNavigationLayout.Default.ViewportHeight,
            scrollOffset);
    }

    private static OblivionWorkspacePage GetPersistedCardsPage()
    {
        OblivionWorkspaceLoadResult load = OblivionWorkbenchCatalog.LoadWorkspace(useCache: false);
        return Assert.Single(
            load.Workspace!.Sections.SelectMany(section => section.Pages),
            page => page.PresenterPageId == OblivionWorkbenchCatalog.CardsPageId);
    }

    private static double ComputeOblivionBodyTop(OblivionCard card, OblivionCardRenderOptions options)
    {
        double bodyTop = options.TitleHeight;
        if (!string.IsNullOrWhiteSpace(card.Subtitle))
        {
            bodyTop += options.SmallGap + options.SubtitleHeight;
        }

        bodyTop += options.SectionGap + options.RowHeight;
        if (card.Tags.Count > 0)
        {
            bodyTop += options.SmallGap + options.RowHeight;
        }

        bodyTop += options.SectionGap;
        return bodyTop;
    }

    private static double ComputeOblivionFooterHeight(OblivionCard card, OblivionCardRenderOptions options)
    {
        double footerHeight = 0;
        if (card.Actions.Count > 0)
        {
            footerHeight += options.RowHeight + options.SmallGap;
        }

        if (card.Artifacts.Count > 0)
        {
            footerHeight += options.RowHeight + options.SmallGap;
        }

        return footerHeight;
    }

    private static Rect FindRectBySuffix(Machina.Layout.Documents.ResolvedLayoutDocument resolved, string suffix)
    {
        foreach ((Machina.Layout.Rows.NodeId nodeId, Machina.Layout.Documents.ResolvedLayoutNode node) in resolved.Nodes)
        {
            if (nodeId.Value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return node.Rect;
            }
        }

        throw new KeyNotFoundException($"No resolved node ended with '{suffix}'.");
    }

    private static int CountBodyLines(PresenterPageRenderResult page, string cardId)
    {
        return page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Count(command =>
                command.Id.Contains(cardId, StringComparison.Ordinal) &&
                command.Id.Contains(".body-line-", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> CreateLines(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => $"line {index} keeps enough width for deterministic clipping checks")
            .ToArray();
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m11e-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void AssertRectInside(Rect inner, Rect outer, string id)
    {
        Assert.True(inner.X >= outer.X, $"{id} left outside");
        Assert.True(inner.Y >= outer.Y, $"{id} top outside");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{id} right outside");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{id} bottom outside");
    }
}
