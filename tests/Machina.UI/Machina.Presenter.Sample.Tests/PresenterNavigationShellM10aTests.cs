using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterNavigationShellM10aTests
{
    [Fact]
    public void PresenterNavigationState_DefaultsToFirstSectionAndTab()
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(model);

        Assert.Equal("overview", state.SelectedSectionId);
        Assert.Equal("home", state.GetSelectedTabId("overview", model));
    }

    [Fact]
    public void PresenterNavigationState_SelectSectionUpdatesSelectedSection()
    {
        PresenterNavigationState next = Dispatch(PresenterNavigationState.CreateDefault(Model), PresenterNavigationActions.SelectSection("components"));

        Assert.Equal("components", next.SelectedSectionId);
        Assert.Equal("controls", next.GetSelectedTabId("components", Model));
    }

    [Fact]
    public void PresenterNavigationState_SelectSectionRestoresLastSelectedTab()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        state = Dispatch(state, PresenterNavigationActions.SelectTab("components", "cards"));
        state = Dispatch(state, PresenterNavigationActions.SelectSection("overview"));
        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SelectSection("components"));

        Assert.Equal("components", next.SelectedSectionId);
        Assert.Equal("cards", next.GetSelectedTabId("components", Model));
    }

    [Fact]
    public void PresenterNavigationState_SelectTabIsScopedToSection()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SelectTab("components", "cards"));

        Assert.Equal("home", next.GetSelectedTabId("overview", Model));
        Assert.Equal("cards", next.GetSelectedTabId("components", Model));
    }

    [Fact]
    public void PresenterNavigationState_InvalidSectionIsIgnoredOrRejectedDeterministically()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SelectSection("missing"));

        Assert.Equal(state, next);
    }

    [Fact]
    public void PresenterNavigationState_InvalidTabIsIgnoredOrRejectedDeterministically()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SelectTab("overview", "missing"));

        Assert.Equal(state, next);
    }

    [Fact]
    public void PresenterNavigationDispatch_SelectSection()
    {
        PresenterNavigationState next = Dispatch(PresenterNavigationState.CreateDefault(Model), PresenterNavigationActions.SelectSection("text"));

        Assert.Equal("text", next.SelectedSectionId);
    }

    [Fact]
    public void PresenterNavigationDispatch_SelectTab()
    {
        PresenterNavigationState next = Dispatch(PresenterNavigationState.CreateDefault(Model), PresenterNavigationActions.SelectTab("text", "proofs"));

        Assert.Equal("text", next.SelectedSectionId);
        Assert.Equal("proofs", next.GetSelectedTabId("text", Model));
    }

    [Fact]
    public void PresenterNavigationDispatch_SetScrollOffset()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SetScrollOffset("components.controls", 120));

        Assert.Equal(120, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void PresenterNavigationDispatch_ClampsScrollOffset()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SetScrollOffset("components.controls", 9999));

        double expected = PresenterScrollRegion.ComputeMaxScrollOffset(
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", ProofOptions),
            PresenterNavigationLayout.Default.ViewportHeight);

        Assert.Equal(expected, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void PresenterNavigationShell_RendersSidebarSections()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        List<string> texts = render.ShellFrame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Overview", texts);
        Assert.Contains("Components", texts);
        Assert.Contains("Text", texts);
        Assert.Contains("Diagnostics", texts);
        Assert.Contains("Legacy", texts);
    }

    [Fact]
    public void PresenterNavigationShell_HighlightsSelectedSection()
    {
        PresenterNavigationShellRenderResult render = RenderShell();

        FillRectCommand selected = Assert.Single(
            render.ShellFrame.RenderCommands.OfType<FillRectCommand>(),
            command => command.Id == "sidebar-section-overview/sidebar-section-overview.button");

        FillRectCommand unselected = Assert.Single(
            render.ShellFrame.RenderCommands.OfType<FillRectCommand>(),
            command => command.Id == "sidebar-section-components/sidebar-section-components.button");

        Assert.NotEqual(selected.Color, unselected.Color);
    }

    [Fact]
    public void PresenterNavigationShell_RendersTabsForSelectedSection()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        List<string> texts = render.ShellFrame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Home", texts);
        Assert.Contains("Status", texts);
    }

    [Fact]
    public void PresenterNavigationShell_HighlightsSelectedTab()
    {
        PresenterNavigationShellRenderResult render = RenderShell();

        FillRectCommand selected = Assert.Single(
            render.ShellFrame.RenderCommands.OfType<FillRectCommand>(),
            command => command.Id == "tab-overview-home/tab-overview-home.button");

        FillRectCommand unselected = Assert.Single(
            render.ShellFrame.RenderCommands.OfType<FillRectCommand>(),
            command => command.Id == "tab-overview-status/tab-overview-status.button");

        Assert.NotEqual(selected.Color, unselected.Color);
    }

    [Fact]
    public void PresenterNavigationShell_DoesNotRenderTabsFromOtherSections()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        List<string> texts = render.ShellFrame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.DoesNotContain("Controls", texts);
        Assert.DoesNotContain("Cards", texts);
    }

    [Fact]
    public void ScrollRegion_ComputesMaxScrollOffset()
    {
        double result = PresenterScrollRegion.ComputeMaxScrollOffset(600, 200);

        Assert.Equal(400, result);
    }

    [Fact]
    public void ScrollRegion_ClampsScrollOffset()
    {
        double result = PresenterScrollRegion.ClampScrollOffset(600, 200, 450);

        Assert.Equal(400, result);
    }

    [Fact]
    public void ScrollbarGeometry_HidesOrDisablesWhenContentFits()
    {
        ScrollbarGeometry geometry = PresenterScrollRegion.ComputeScrollbarGeometry(new Rect(0, 0, 12, 120), 120, 120, 0);

        Assert.False(geometry.IsVisible);
        Assert.Equal(0, geometry.MaxScrollOffset);
    }

    [Fact]
    public void ScrollbarGeometry_ComputesThumbSizeFromViewportRatio()
    {
        ScrollbarGeometry geometry = PresenterScrollRegion.ComputeScrollbarGeometry(new Rect(0, 0, 12, 120), 480, 240, 0);

        Assert.Equal(60, geometry.ThumbRect.Height);
    }

    [Fact]
    public void ScrollbarGeometry_ComputesThumbPositionFromScrollOffset()
    {
        ScrollbarGeometry geometry = PresenterScrollRegion.ComputeScrollbarGeometry(new Rect(0, 0, 12, 120), 480, 240, 120);

        Assert.Equal(30, geometry.ThumbRect.Y);
    }

    [Fact]
    public void ExportPresenter_WithNavigationShell_WritesArtifact()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-navigation-tests", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(outputDirectory, "presenter-navigation-shell-overview.png");

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                outputPath,
                ProofOptions,
                new PresenterNavigationExportOptions(true, "overview.home"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(result.IncludesNavigationShell);
            Assert.Equal("overview.home", result.NavigationPageId);
            Assert.True(File.Exists(result.NavigationManifestJsonPath!));
            Assert.True(File.Exists(result.NavigationManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_NavigationShellShowsSidebarTabsAndScrollbar()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-navigation-tests", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(outputDirectory, "presenter-navigation-shell-scrolled.png");

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                outputPath,
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    "components.controls",
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["components.controls"] = 120,
                    }),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(result.IncludesNavigationShell);
            Assert.Equal("components.controls", result.NavigationPageId);
            Assert.NotNull(result.ScrollbarGeometry);
            Assert.True(result.ScrollbarGeometry!.IsVisible);
            Assert.True(result.ScrollbarGeometry.MaxScrollOffset > 0);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_DefaultBehavior_UsesNavigationShell()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-navigation-tests", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(outputDirectory, "presenter-default.png");

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                outputPath,
                new PresenterProofOptions(),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(result.IncludesNavigationShell);
            Assert.Equal("overview.home", result.NavigationPageId);
            Assert.NotNull(result.NavigationManifestJsonPath);
            Assert.NotNull(result.NavigationManifestTextPath);
            Assert.Equal(PresenterNavigationLayout.Default.RootWidth, result.Width);
            Assert.Equal(PresenterNavigationLayout.Default.RootHeight, result.Height);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    private static PresenterNavigationModel Model => PresenterNavigationCatalog.CreateModel();

    private static PresenterProofOptions ProofOptions => new();

    private static PresenterNavigationState Dispatch(PresenterNavigationState state, UiActionId actionId)
    {
        return PresenterNavigationDispatch.Dispatch(
            state,
            actionId,
            Model,
            ProofOptions,
            PresenterNavigationLayout.Default);
    }

    private static PresenterNavigationShellRenderResult RenderShell()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            StandardTheme.Default,
            ProofOptions);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
