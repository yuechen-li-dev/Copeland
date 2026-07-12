using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterPageOrganizationM10cTests
{
    [Fact]
    public void Presenter_DefaultRun_UsesNavigationShell()
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse([]);

        Assert.True(options.NavigationOptions.IncludeNavigationShell);
    }

    [Fact]
    public void Presenter_LegacySingleCard_IsStillAvailableIfSupported()
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse(["--legacy-single-card"]);

        Assert.False(options.NavigationOptions.IncludeNavigationShell);
    }

    [Fact]
    public void Presenter_IncludeNavigationShell_RemainsCompatible()
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse(["--legacy-single-card", "--include-navigation-shell"]);

        Assert.True(options.NavigationOptions.IncludeNavigationShell);
    }

    [Fact]
    public void Presenter_DefaultShell_StartsAtOverviewHome()
    {
        PresenterNavigationShellRenderResult render = RenderShell();

        Assert.Equal("overview", render.SelectedSection.Id);
        Assert.Equal("home", render.SelectedTab.Id);
        Assert.Equal("overview.home", render.SelectedTab.PageId);
        Assert.Equal(0, render.NavigationState.GetScrollOffset("overview.home"));
    }

    [Fact]
    public void PresenterShell_ContainsOverviewSection()
    {
        Assert.Contains(Model.Sections, section => section.Id == "overview");
    }

    [Fact]
    public void PresenterShell_ContainsComponentsSection()
    {
        Assert.Contains(Model.Sections, section => section.Id == "components");
    }

    [Fact]
    public void PresenterShell_ContainsTextSection()
    {
        Assert.Contains(Model.Sections, section => section.Id == "text");
    }

    [Fact]
    public void PresenterShell_ContainsDiagnosticsSection()
    {
        Assert.Contains(Model.Sections, section => section.Id == "diagnostics");
    }

    [Fact]
    public void PresenterShell_ContainsLegacySection()
    {
        Assert.Contains(Model.Sections, section => section.Id == "legacy");
    }

    [Fact]
    public void PresenterShell_LegacySectionContainsM1eCard()
    {
        PresenterNavigationSection legacy = Assert.Single(Model.Sections, section => section.Id == "legacy");
        PresenterNavigationTab tab = Assert.Single(legacy.Tabs);

        Assert.Equal("m1e-card", tab.Id);
        Assert.Equal("legacy.m1e-card", tab.PageId);
    }

    [Fact]
    public void PresenterShell_ExistingIncrementControlStillAppears()
    {
        PresenterPageRenderResult page = RenderPage("components.controls");
        List<string> texts = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Increment", texts);
    }

    [Fact]
    public void PresenterShell_ExistingEmailNotificationControlsStillAppear()
    {
        PresenterPageRenderResult page = RenderPage("components.controls");
        List<string> texts = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains(texts, text => text.Contains("Email updates", StringComparison.Ordinal));
        Assert.Contains(texts, text => text.Contains("Notifications", StringComparison.Ordinal));
    }

    [Fact]
    public void PresenterShell_SelectedSectionAndTabResolvePage()
    {
        PresenterNavigationState state = PresenterNavigationCatalog.CreateState(
            Model,
            ProofOptions,
            new PresenterNavigationExportOptions(
                true,
                SelectedSectionId: "text",
                SelectedTabId: "direct-outline"));

        PresenterNavigationShellRenderResult render = RenderShell(state);

        Assert.Equal("text", render.SelectedSection.Id);
        Assert.Equal("direct-outline", render.SelectedTab.Id);
        Assert.Equal("text.direct-outline", render.SelectedTab.PageId);
    }

    [Fact]
    public void PresenterShell_SelectedSectionTabExportsExpectedPage()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-text-direct-outline.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "text",
                    SelectedTabId: "direct-outline"),
                StandardTheme.Default);

            Assert.True(result.IncludesNavigationShell);
            Assert.Equal("text", result.NavigationSectionId);
            Assert.Equal("direct-outline", result.NavigationTabId);
            Assert.Equal("text.direct-outline", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void PresenterShell_InvalidSelectedSectionFallsBackDeterministically()
    {
        PresenterNavigationState state = PresenterNavigationCatalog.CreateState(
            Model,
            ProofOptions,
            new PresenterNavigationExportOptions(
                true,
                SelectedSectionId: "missing"));

        PresenterNavigationShellRenderResult render = RenderShell(state);

        Assert.Equal("overview", render.SelectedSection.Id);
        Assert.Equal("home", render.SelectedTab.Id);
    }

    [Fact]
    public void PresenterShell_InvalidSelectedTabFallsBackDeterministically()
    {
        PresenterNavigationState state = PresenterNavigationCatalog.CreateState(
            Model,
            ProofOptions,
            new PresenterNavigationExportOptions(
                true,
                SelectedSectionId: "text",
                SelectedTabId: "missing"));

        PresenterNavigationShellRenderResult render = RenderShell(state);

        Assert.Equal("text", render.SelectedSection.Id);
        Assert.Equal("current", render.SelectedTab.Id);
    }

    [Fact]
    public void PresenterShell_ClickSidebarStillSelectsSection()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            candidate => string.Equals(candidate.SectionId, "components", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal("components", next.SelectedSectionId);
    }

    [Fact]
    public void PresenterShell_ClickTabStillSelectsLocalTab()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            candidate => string.Equals(candidate.TabId, "status", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal("status", next.GetSelectedTabId("overview", Model));
    }

    [Fact]
    public void PresenterShell_WheelScrollStillUpdatesSelectedPage()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedTab("components", "controls")
            .WithSelectedSection("components");
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), -1));

        Assert.Equal(PresenterNavigationInputRouter.ScrollWheelMultiplier, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void ExportPresenter_DefaultWritesShellArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-overview.png"),
                ProofOptions,
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
    public void ExportPresenter_SelectedComponentsControlsWritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-components-controls.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "components",
                    SelectedTabId: "controls"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("components.controls", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_LegacyM1eCardPageWritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-legacy-m1e-card.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "legacy",
                    SelectedTabId: "m1e-card"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("legacy", result.NavigationSectionId);
            Assert.Equal("m1e-card", result.NavigationTabId);
            Assert.Equal("legacy.m1e-card", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_ScrolledShellWritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-scrolled.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "components",
                    SelectedTabId: "controls",
                    ScrollOffsetByPageId: new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["components.controls"] = 120,
                    }),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.NotNull(result.ScrollbarGeometry);
            Assert.True(result.ScrollbarGeometry!.IsVisible);
            Assert.True(result.ScrollbarGeometry.ScrollOffset > 0);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    private static PresenterNavigationModel Model => PresenterNavigationCatalog.CreateModel();

    private static PresenterProofOptions ProofOptions => new();

    private static PresenterPageRenderResult RenderPage(string pageId, PresenterProofOptions? proofOptions = null)
    {
        return PresenterNavigationCatalog.RenderPage(
            pageId,
            DemoState.Default,
            StandardTheme.Default,
            proofOptions ?? ProofOptions,
            PresenterNavigationLayout.Default.ContentVisibleWidth);
    }

    private static PresenterNavigationShellRenderResult RenderShell(PresenterNavigationState? state = null, PresenterProofOptions? proofOptions = null)
    {
        PresenterProofOptions effectiveProofOptions = proofOptions ?? ProofOptions;
        PresenterNavigationState current = state ?? PresenterNavigationState.CreateDefault(Model);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            current,
            StandardTheme.Default,
            effectiveProofOptions);
    }

    private static PresenterNavigationState DispatchInput(PresenterNavigationState state, PresenterInputEvent inputEvent)
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

    private static PresenterInputEvent PointerPress(PresenterInputPoint point)
    {
        return new PresenterInputEvent(
            PresenterInputKind.PointerPressed,
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

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m10c-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
