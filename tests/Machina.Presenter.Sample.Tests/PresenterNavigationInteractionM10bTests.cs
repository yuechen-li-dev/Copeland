using System.Reflection;
using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterNavigationInteractionM10bTests
{
    [Fact]
    public void AvaloniaInputBackend_IsSampleScoped()
    {
        Assert.Equal(
            typeof(PresenterNavigationState).Assembly,
            typeof(AvaloniaPresenterInputBackend).Assembly);
        Assert.StartsWith("Machina.Presenter.Sample", typeof(AvaloniaPresenterInputBackend).Namespace, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterNavigationState_DoesNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterNavigationState));
    }

    [Fact]
    public void PresenterNavigationDispatch_DoesNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterNavigationDispatch));
    }

    [Fact]
    public void PresenterNavigationHitTesting_DoesNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterNavigationHitTesting));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterNavigationChromeGeometry));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(PresenterNavigationInputRouter));
    }

    [Fact]
    public void NavigationHitTest_HitsSidebarSection()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            candidate => string.Equals(candidate.SectionId, "components", StringComparison.Ordinal));

        PresenterNavigationHitTarget hit = PresenterNavigationHitTesting.HitTest(render.ChromeGeometry, Center(region.Rect));

        Assert.Equal(PresenterNavigationHitKind.SidebarSection, hit.Kind);
        Assert.Equal("components", hit.SectionId);
    }

    [Fact]
    public void NavigationHitTest_HitsSelectedSectionTabs()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            candidate => string.Equals(candidate.TabId, "status", StringComparison.Ordinal));

        PresenterNavigationHitTarget hit = PresenterNavigationHitTesting.HitTest(render.ChromeGeometry, Center(region.Rect));

        Assert.Equal(PresenterNavigationHitKind.LocalTab, hit.Kind);
        Assert.Equal("overview", hit.SectionId);
        Assert.Equal("status", hit.TabId);
        Assert.Equal("overview.status", hit.PageId);
    }

    [Fact]
    public void NavigationHitTest_HitsContentViewport()
    {
        PresenterNavigationShellRenderResult render = RenderShell();

        PresenterNavigationHitTarget hit = PresenterNavigationHitTesting.HitTest(
            render.ChromeGeometry,
            Center(render.ChromeGeometry.ContentViewportRect));

        Assert.Equal(PresenterNavigationHitKind.ContentViewport, hit.Kind);
    }

    [Fact]
    public void NavigationHitTest_HitsScrollbarWhenVisible()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedTab("components", "controls")
            .WithSelectedSection("components")
            .WithScrollOffset("components.controls", 120);
        PresenterNavigationShellRenderResult render = RenderShell(state);

        Assert.True(render.ScrollbarGeometry.IsVisible);

        PresenterNavigationHitTarget hit = PresenterNavigationHitTesting.HitTest(
            render.ChromeGeometry,
            Center(render.ScrollbarGeometry.ThumbRect));

        Assert.Equal(PresenterNavigationHitKind.ScrollbarThumb, hit.Kind);
    }

    [Fact]
    public void NavigationHitTest_ReturnsNoneForBackground()
    {
        PresenterNavigationShellRenderResult render = RenderShell();

        PresenterNavigationHitTarget hit = PresenterNavigationHitTesting.HitTest(
            render.ChromeGeometry,
            new PresenterInputPoint(2, 2));

        Assert.Equal(PresenterNavigationHitKind.None, hit.Kind);
    }

    [Fact]
    public void NavigationInput_ClickSidebar_SelectsSection()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            candidate => string.Equals(candidate.SectionId, "components", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal("components", next.SelectedSectionId);
    }

    [Fact]
    public void NavigationInput_ClickSidebar_RestoresSectionTab()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        state = Dispatch(state, PresenterNavigationActions.SelectSection("components"));
        state = Dispatch(state, PresenterNavigationActions.SelectTab("components", "cards"));
        state = Dispatch(state, PresenterNavigationActions.SelectSection("overview"));

        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            candidate => string.Equals(candidate.SectionId, "components", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal("components", next.SelectedSectionId);
        Assert.Equal("cards", next.GetSelectedTabId("components", Model));
    }

    [Fact]
    public void NavigationInput_ClickSidebar_PreservesPageScrollOffsets()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        state = Dispatch(state, PresenterNavigationActions.SelectSection("components"));
        state = Dispatch(state, PresenterNavigationActions.SelectTab("components", "controls"));
        state = Dispatch(state, PresenterNavigationActions.SetScrollOffset("components.controls", 120));
        state = Dispatch(state, PresenterNavigationActions.SelectSection("overview"));

        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            candidate => string.Equals(candidate.SectionId, "components", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal(120, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void NavigationInput_ClickTab_SelectsTab()
    {
        PresenterNavigationShellRenderResult render = RenderShell();
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            candidate => string.Equals(candidate.TabId, "status", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal("overview", next.SelectedSectionId);
        Assert.Equal("status", next.GetSelectedTabId("overview", Model));
    }

    [Fact]
    public void NavigationInput_ClickTab_IsScopedToSelectedSection()
    {
        PresenterNavigationShellRenderResult render = RenderShell();

        Assert.All(
            render.ChromeGeometry.LocalTabs,
            region => Assert.Equal("overview", region.SectionId));
        Assert.DoesNotContain(
            render.ChromeGeometry.LocalTabs,
            region => string.Equals(region.SectionId, "components", StringComparison.Ordinal));
    }

    [Fact]
    public void NavigationInput_ClickTab_RestoresPageScrollOffset()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        state = Dispatch(state, PresenterNavigationActions.SelectSection("components"));
        state = Dispatch(state, PresenterNavigationActions.SelectTab("components", "controls"));
        state = Dispatch(state, PresenterNavigationActions.SetScrollOffset("components.controls", 40));
        state = Dispatch(state, PresenterNavigationActions.SelectTab("components", "cards"));

        PresenterNavigationShellRenderResult render = RenderShell(state);
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            candidate => string.Equals(candidate.TabId, "controls", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)));

        Assert.Equal(40, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void NavigationInput_WheelOverContent_UpdatesScrollOffset()
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
    public void NavigationInput_WheelOverContent_ClampsAtTop()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedTab("components", "controls")
            .WithSelectedSection("components");
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), 10));

        Assert.Equal(0, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void NavigationInput_WheelOverContent_ClampsAtBottom()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedTab("components", "controls")
            .WithSelectedSection("components");
        PresenterNavigationShellRenderResult render = RenderShell(state);
        double expected = PresenterScrollRegion.ComputeMaxScrollOffset(
            PresenterNavigationCatalog.GetPageContentHeight("components.controls", ProofOptions),
            PresenterNavigationLayout.Default.ViewportHeight);

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), -1000));

        Assert.Equal(expected, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void NavigationInput_WheelOutsideContent_DoesNotScroll()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedTab("components", "controls")
            .WithSelectedSection("components");
        PresenterNavigationShellRenderResult render = RenderShell(state);

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            Wheel(new PresenterInputPoint(2, 2), -1));

        Assert.Equal(0, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void NavigationInput_WheelWhenContentFits_DoesNotScroll()
    {
        PresenterNavigationShellRenderResult render = RenderShell();

        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
            render,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), -1));

        Assert.Null(routed.ActionId);
        Assert.Equal(0, render.NavigationState.GetScrollOffset("overview.home"));
    }

    [Fact]
    public void ExportPresenter_NavigationInteractionArtifacts_Write()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-navigation-m10b-tests", Guid.NewGuid().ToString("N"));

        try
        {
            PresenterExportResult overview = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-navigation-interaction-overview.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    InteractionBackendName: AvaloniaPresenterInputBackend.BackendName),
                StandardTheme.Default);

            PresenterExportResult components = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-navigation-interaction-components-selected.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "components",
                    InteractionBackendName: AvaloniaPresenterInputBackend.BackendName),
                StandardTheme.Default);

            PresenterExportResult tab = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-navigation-interaction-tab-selected.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "components",
                    SelectedTabId: "controls",
                    InteractionBackendName: AvaloniaPresenterInputBackend.BackendName),
                StandardTheme.Default);

            PresenterExportResult scrolled = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-navigation-interaction-scrolled.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "components",
                    SelectedTabId: "controls",
                    ScrollOffsetByPageId: new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["components.controls"] = 120,
                    },
                    InteractionBackendName: AvaloniaPresenterInputBackend.BackendName),
                StandardTheme.Default);

            Assert.True(File.Exists(overview.OutputPath));
            Assert.True(File.Exists(components.OutputPath));
            Assert.True(File.Exists(tab.OutputPath));
            Assert.True(File.Exists(scrolled.OutputPath));
            Assert.Equal("components", components.NavigationSectionId);
            Assert.Equal("controls", tab.NavigationTabId);
            Assert.NotNull(scrolled.ScrollbarGeometry);
            Assert.Equal(
                Path.Combine(outputDirectory, PresenterNavigationManifestWriter.JsonFileName),
                scrolled.NavigationManifestJsonPath);
            Assert.Equal(
                Path.Combine(outputDirectory, PresenterNavigationManifestWriter.TextFileName),
                scrolled.NavigationManifestTextPath);
            Assert.True(File.Exists(scrolled.NavigationManifestJsonPath!));
            Assert.True(File.Exists(scrolled.NavigationManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void Presenter_DefaultBehavior_RemainsUnchangedWithoutNavigationShell()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-navigation-m10b-tests", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(outputDirectory, "presenter-default.png");

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                outputPath,
                ProofOptions,
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.False(result.IncludesNavigationShell);
            Assert.Null(result.NavigationSectionId);
            Assert.Null(result.NavigationManifestJsonPath);
            Assert.Equal(SettingsScreen.RootWidth, result.Width);
            Assert.Equal(SettingsScreen.RootHeight, result.Height);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void Presenter_NavigationShellInteraction_IsOptIn()
    {
        PresenterExportResult result = PresenterExporter.Export(
            DemoState.Default,
            Path.Combine(Path.GetTempPath(), "presenter-navigation-opt-in.png"),
            ProofOptions,
            new PresenterNavigationExportOptions(
                true,
                InteractionBackendName: AvaloniaPresenterInputBackend.BackendName),
            StandardTheme.Default);

        try
        {
            Assert.True(result.IncludesNavigationShell);
            Assert.Equal("overview", result.NavigationSectionId);
            Assert.Equal("home", result.NavigationTabId);
            Assert.Equal("overview.home", result.NavigationPageId);
        }
        finally
        {
            if (File.Exists(result.OutputPath))
            {
                File.Delete(result.OutputPath);
            }
        }
    }

    private static PresenterNavigationModel Model => PresenterNavigationCatalog.CreateModel();

    private static PresenterProofOptions ProofOptions => new();

    private static PresenterNavigationState Dispatch(PresenterNavigationState state, UiActionId actionId, PresenterProofOptions? proofOptions = null)
    {
        return PresenterNavigationDispatch.Dispatch(
            state,
            actionId,
            Model,
            proofOptions ?? ProofOptions,
            PresenterNavigationLayout.Default);
    }

    private static PresenterNavigationState DispatchInput(
        PresenterNavigationState state,
        PresenterInputEvent inputEvent,
        PresenterProofOptions? proofOptions = null)
    {
        PresenterProofOptions effectiveProofOptions = proofOptions ?? ProofOptions;
        PresenterNavigationShellRenderResult render = RenderShell(state, effectiveProofOptions);
        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, inputEvent);
        if (routed.ActionId is null)
        {
            return render.NavigationState;
        }

        return Dispatch(render.NavigationState, routed.ActionId.Value, effectiveProofOptions);
    }

    private static PresenterNavigationShellRenderResult RenderShell(
        PresenterNavigationState? state = null,
        PresenterProofOptions? proofOptions = null)
    {
        PresenterNavigationState current = state ?? PresenterNavigationState.CreateDefault(Model);
        PresenterProofOptions effectiveProofOptions = proofOptions ?? ProofOptions;
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            current,
            StandardTheme.Default,
            effectiveProofOptions);
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

    private static void AssertTypeSurfaceDoesNotReferenceAvalonia(Type type)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            AssertNoAvaloniaType(property.PropertyType);
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            AssertNoAvaloniaType(field.FieldType);
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                AssertNoAvaloniaType(parameter.ParameterType);
            }
        }

        foreach (MethodInfo method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            AssertNoAvaloniaType(method.ReturnType);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                AssertNoAvaloniaType(parameter.ParameterType);
            }
        }
    }

    private static void AssertNoAvaloniaType(Type type)
    {
        if (type == typeof(void))
        {
            return;
        }

        Assert.False(
            type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true,
            $"Unexpected Avalonia type reference: {type.FullName}");

        if (type.IsArray)
        {
            AssertNoAvaloniaType(type.GetElementType()!);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (Type genericArgument in type.GetGenericArguments())
            {
                AssertNoAvaloniaType(genericArgument);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
