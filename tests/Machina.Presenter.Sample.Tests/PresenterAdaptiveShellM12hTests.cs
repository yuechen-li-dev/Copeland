using System.Reflection;
using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Runtime.Input;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterAdaptiveShellM12hTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();

    [Fact]
    public void PresenterShellModeResolver_WidthBelowBreakpoint_IsCompact()
    {
        Assert.Equal(
            PresenterShellMode.Compact,
            PresenterShellModeResolver.Resolve(PresenterShellModeResolver.BreakpointWidth - 1));
    }

    [Fact]
    public void PresenterShellModeResolver_WidthAtBreakpoint_IsWide()
    {
        Assert.Equal(
            PresenterShellMode.Wide,
            PresenterShellModeResolver.Resolve(PresenterShellModeResolver.BreakpointWidth));
    }

    [Fact]
    public void PresenterShellModeResolver_WidthAboveBreakpoint_IsWide()
    {
        Assert.Equal(
            PresenterShellMode.Wide,
            PresenterShellModeResolver.Resolve(PresenterShellModeResolver.BreakpointWidth + 1));
    }

    [Fact]
    public void PresenterShellModeResolver_UsesDocumentedBreakpoint()
    {
        Assert.Equal(1120, PresenterShellModeResolver.BreakpointWidth);
    }

    [Fact]
    public void PresenterWideShell_RendersSidebarTabsContent()
    {
        PresenterNavigationShellRenderResult render = RenderShell(width: 1120, height: 760);

        Assert.Equal(PresenterShellMode.Wide, render.ShellMode);
        Assert.NotEmpty(render.ChromeGeometry.SidebarSections);
        Assert.NotEmpty(render.ChromeGeometry.LocalTabs);
        Assert.Contains(render.PageFrame.RenderCommands.OfType<DrawTextCommand>(), command => command.Text.Contains("Canonical presenter sample surface", StringComparison.Ordinal));
    }

    [Fact]
    public void PresenterWideShell_OblivionDocsShowsCardListAndInspector()
    {
        PresenterNavigationState state = OblivionDocsState("doc-copeland-markdown-frontend-m12a");
        PresenterPageRenderResult page = PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            DemoState.Default,
            StandardTheme.Default,
            ProofOptions,
            PresenterNavigationLayout.Create(1120, 760, PresenterShellMode.Wide).ContentVisibleWidth,
            state,
            PresenterShellMode.Wide);

        string text = PageText(page);

        Assert.Contains(page.Document.Rows, row => string.Equals(row.Id.Value, "oblivion.docs.cards-panel", StringComparison.Ordinal));
        Assert.Contains(page.Document.Rows, row => string.Equals(row.Id.Value, "oblivion.docs.inspector-panel", StringComparison.Ordinal));
        Assert.Contains("Selected card inspector", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterWideShell_PreservesExistingNavigationBehavior()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("overview");

        PresenterNavigationShellRenderResult render = RenderShell(state, width: 1120, height: 760);
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            candidate => string.Equals(candidate.SectionId, "components", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(state, PointerPress(Center(region.Rect)), 1120, 760);

        Assert.Equal("components", next.SelectedSectionId);
    }

    [Fact]
    public void PresenterCompactShell_RendersSidebarRail()
    {
        PresenterNavigationShellRenderResult render = RenderShell(width: 720, height: 760);
        string shellText = ShellText(render);

        Assert.Equal(PresenterShellMode.Compact, render.ShellMode);
        Assert.Equal(64, render.Layout.SidebarWidth);
        Assert.Contains("OVR", shellText, StringComparison.Ordinal);
        Assert.Contains("CMP", shellText, StringComparison.Ordinal);
        Assert.Contains("OBL", shellText, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterCompactShell_DefaultsToCardListPane()
    {
        PresenterNavigationState state = OblivionDocsState();
        PresenterNavigationShellRenderResult render = RenderShell(state, width: 720, height: 760);

        Assert.Equal(PresenterCompactPane.CardList, render.NavigationState.CompactPane);
        Assert.Contains("Card list", PageText(render.PageRender!), StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterCompactShell_SelectingCardExpandsInCardListPane()
    {
        PresenterNavigationState state = OblivionDocsState();
        PresenterNavigationShellRenderResult render = RenderShell(state, width: 720, height: 760);
        OblivionCardHitTarget target = render.PageRender!.OblivionInteraction!.CardTargets.First();

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            PointerPress(ToRootPoint(render, target.Bounds)),
            720,
            760);

        Assert.Equal(PresenterCompactPane.CardList, next.CompactPane);
        Assert.Equal(target.CardId, next.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)));
        Assert.True(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, target.CardId).IsExpanded);
    }

    [Fact]
    public void PresenterCompactShell_BackReturnsToCardListPane()
    {
        PresenterNavigationState state = OblivionDocsState("doc-copeland-markdown-frontend-m12a")
            .WithCompactPane(PresenterCompactPane.Inspector);
        PresenterNavigationShellRenderResult render = RenderShell(state, width: 720, height: 760);

        UiAction? action = render.HitTestContent(new PointerPoint(
            render.Layout.ViewportRect.X + 60,
            render.Layout.ViewportRect.Y + 18));
        Assert.NotNull(action);

        PresenterNavigationState next = Dispatch(render.NavigationState, action!.Id, render.Layout);

        Assert.Equal(PresenterCompactPane.CardList, next.CompactPane);
    }

    [Fact]
    public void PresenterCompactShell_SectionChangeResetsToCardListPane()
    {
        PresenterNavigationState state = OblivionDocsState("doc-copeland-markdown-frontend-m12a")
            .WithCompactPane(PresenterCompactPane.Inspector);

        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SelectSection("components"), CompactLayout());

        Assert.Equal(PresenterCompactPane.CardList, next.CompactPane);
    }

    [Fact]
    public void PresenterCompactShell_TabChangeResetsToCardListPane()
    {
        PresenterNavigationState state = OblivionDocsState("doc-copeland-markdown-frontend-m12a")
            .WithCompactPane(PresenterCompactPane.Inspector);

        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SelectTab("oblivion", "cards"), CompactLayout());

        Assert.Equal(PresenterCompactPane.CardList, next.CompactPane);
    }

    [Fact]
    public void PresenterCompactShell_PreservesSelectedCardAcrossModeSwitch()
    {
        PresenterNavigationState state = OblivionDocsState("doc-copeland-markdown-frontend-m12a");

        PresenterNavigationShellRenderResult wide = RenderShell(state, width: 1120, height: 760);
        PresenterNavigationShellRenderResult compact = RenderShell(state, width: 720, height: 760);

        Assert.Equal(
            wide.NavigationState.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)),
            compact.NavigationState.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)));
    }

    [Fact]
    public void PresenterAdaptiveShell_CardsDoNotKnowShellMode()
    {
        string cardRendererSource = File.ReadAllText(Path.Combine(RepoRoot, "samples", "Machina.Presenter.Sample", "OblivionCardRenderer.cs"));

        Assert.DoesNotContain("PresenterShellMode", cardRendererSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterAdaptiveShell_BreakpointIsResolvedAtTopLevel()
    {
        string stateSource = File.ReadAllText(Path.Combine(RepoRoot, "samples", "Machina.Presenter.Sample", "PresenterNavigationCatalog.cs"));
        string cardSource = File.ReadAllText(Path.Combine(RepoRoot, "samples", "Machina.Presenter.Sample", "OblivionCardRenderer.cs"));

        Assert.Contains("PresenterShellModeResolver.Resolve", stateSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PresenterShellModeResolver.Resolve", cardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterAdaptiveShell_DoesNotAddContinuousScaling()
    {
        string source = ReadSampleSource();

        Assert.DoesNotContain("continuous interpolation", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("constraint solver", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresenterAdaptiveShell_DoesNotAddGenericResponsiveSolver()
    {
        string source = ReadSampleSource();

        Assert.DoesNotContain("MediaQuery", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConstraintSolver", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactSidebar_ClickSelectsSection()
    {
        PresenterNavigationShellRenderResult render = RenderShell(width: 720, height: 760);
        PresenterNavigationSidebarHitRegion region = Assert.Single(
            render.ChromeGeometry.SidebarSections,
            candidate => string.Equals(candidate.SectionId, "oblivion", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)), 720, 760);

        Assert.Equal("oblivion", next.SelectedSectionId);
    }

    [Fact]
    public void CompactTabs_ClickSelectsTab()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "cards");
        PresenterNavigationShellRenderResult render = RenderShell(state, width: 720, height: 760);
        PresenterNavigationTabHitRegion region = Assert.Single(
            render.ChromeGeometry.LocalTabs,
            candidate => string.Equals(candidate.TabId, "docs", StringComparison.Ordinal));

        PresenterNavigationState next = DispatchInput(render.NavigationState, PointerPress(Center(region.Rect)), 720, 760);

        Assert.Equal("docs", next.GetSelectedTabId("oblivion", Model));
    }

    [Fact]
    public void CompactInspector_EscapeReturnsToCardList()
    {
        PresenterNavigationState state = OblivionDocsState("doc-copeland-markdown-frontend-m12a")
            .WithCompactPane(PresenterCompactPane.Inspector);

        PresenterNavigationState next = DispatchInput(state, KeyDown(PresenterKey.Escape), 720, 760);

        Assert.Equal(PresenterCompactPane.CardList, next.CompactPane);
    }

    [Fact]
    public void KeyboardNavigation_StillWorksInWideMode()
    {
        PresenterNavigationState next = DispatchInput(
            PresenterNavigationState.CreateDefault(Model),
            KeyDown(PresenterKey.ArrowRight, ctrl: true),
            1120,
            760);

        Assert.Equal("status", next.GetSelectedTabId("overview", Model));
    }

    [Fact]
    public void KeyboardNavigation_StillWorksInCompactMode()
    {
        PresenterNavigationState next = DispatchInput(
            PresenterNavigationState.CreateDefault(Model),
            KeyDown(PresenterKey.ArrowDown, ctrl: true),
            720,
            760);

        Assert.Equal("components", next.SelectedSectionId);
    }

    [Fact]
    public void WheelScroll_StillWorksInCompactMode()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls");
        PresenterNavigationShellRenderResult render = RenderShell(state, width: 720, height: 760);

        PresenterNavigationState next = DispatchInput(
            render.NavigationState,
            Wheel(Center(render.ChromeGeometry.ContentViewportRect), -1),
            720,
            760);

        Assert.Equal(PresenterNavigationInputRouter.ScrollWheelMultiplier, next.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void ScrollbarDrag_StillWorksInWideMode()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", 120);
        PresenterNavigationShellRenderResult render = RenderShell(state, width: 1120, height: 760);
        PresenterInputPoint thumbCenter = Center(render.ScrollbarGeometry.ThumbRect);

        PresenterNavigationState next = DispatchSequence(
            state,
            [
                PointerPress(thumbCenter),
                PointerMove(new PresenterInputPoint(thumbCenter.X, thumbCenter.Y + 80)),
                PointerRelease(new PresenterInputPoint(thumbCenter.X, thumbCenter.Y + 80)),
            ],
            1120,
            760);

        Assert.True(next.GetScrollOffset("components.controls") > 120);
    }

    [Fact]
    public void ExportPresenter_WideShell_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-wide-oblivion-docs.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    SelectedCardId: "doc-copeland-markdown-frontend-m12a",
                    Width: 1120,
                    Height: 760),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.AdaptiveShellManifestJsonPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_CompactCardList_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-compact-card-list.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    Width: 720,
                    Height: 760),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("oblivion.docs", result.NavigationPageId);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_CompactInspector_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-shell-compact-inspector.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    SelectedCardId: "doc-copeland-markdown-frontend-m12a",
                    CompactPane: PresenterCompactPane.Inspector,
                    Width: 720,
                    Height: 760),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.AdaptiveShellManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M12hManifest_RecordsNoContinuousScaling()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterNavigationShellRenderResult render = RenderShell(width: 720, height: 760);
            (string jsonPath, string textPath) = PresenterAdaptiveShellManifestWriter.Write(outputDirectory, render);
            string json = File.ReadAllText(jsonPath);
            string text = File.ReadAllText(textPath);

            Assert.Contains("\"continuousScaling\": false", json, StringComparison.Ordinal);
            Assert.Contains("layoutNegotiation=false", text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M12h_DoesNotImplementMarkdownEditor()
    {
        PresenterNavigationState state = OblivionDocsState("doc-copeland-markdown-frontend-m12a");

        PresenterNavigationState next = DispatchInput(state, TextInput("edited"), 720, 760);

        Assert.Equal(state.SelectedSectionId, next.SelectedSectionId);
        Assert.Equal(state.CompactPane, next.CompactPane);
        Assert.Equal(
            state.GetSelectedTabId("oblivion", Model),
            next.GetSelectedTabId("oblivion", Model));
        Assert.Equal(
            state.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)),
            next.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)));
        Assert.Equal(state.EffectState, next.EffectState);
    }

    [Fact]
    public void M12h_DoesNotImplementRoslynExecution()
    {
        string source = ReadSampleSource();

        Assert.DoesNotContain("Microsoft.CodeAnalysis", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", source, StringComparison.Ordinal);
    }

    [Fact]
    public void M12h_DoesNotImplementVisionary()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            string manifest = File.ReadAllText(PresenterAdaptiveShellManifestWriter.Write(outputDirectory, RenderShell(width: 720, height: 760)).jsonPath);

            Assert.Contains("\"visionaryImplemented\": false", manifest, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    private static PresenterNavigationShellRenderResult RenderShell(
        PresenterNavigationState? state = null,
        int width = 1120,
        int height = 760)
    {
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        PresenterNavigationState current = state ?? PresenterNavigationCatalog.CreateState(
            Model,
            ProofOptions,
            new PresenterNavigationExportOptions(true, Width: width, Height: height));
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            current,
            StandardTheme.Default,
            ProofOptions,
            layout);
    }

    private static PresenterNavigationState DispatchInput(
        PresenterNavigationState state,
        PresenterInputEvent inputEvent,
        int width,
        int height)
    {
        PresenterNavigationShellRenderResult render = RenderShell(state, width, height);
        PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, inputEvent);
        UiActionId? actionId = routed.ActionId;

        if (actionId is null &&
            inputEvent.Kind == PresenterInputKind.PointerPressed)
        {
            UiAction? contentAction = render.HitTestContent(new PointerPoint(inputEvent.Position.X, inputEvent.Position.Y));
            actionId = contentAction?.Id;
        }

        if (actionId is null)
        {
            return render.NavigationState;
        }

        return Dispatch(render.NavigationState, actionId.Value, render.Layout);
    }

    private static PresenterNavigationState DispatchSequence(
        PresenterNavigationState initialState,
        IReadOnlyList<PresenterInputEvent> inputEvents,
        int width,
        int height)
    {
        PresenterNavigationState state = initialState;
        PresenterScrollbarInteractionState interactionState = PresenterScrollbarInteractionState.Default;

        foreach (PresenterInputEvent inputEvent in inputEvents)
        {
            PresenterNavigationShellRenderResult render = RenderShell(state, width, height);
            PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(render, inputEvent, interactionState);
            interactionState = routed.InteractionState;
            if (routed.ActionId is not null)
            {
                state = Dispatch(render.NavigationState, routed.ActionId.Value, render.Layout);
            }
            else
            {
                state = render.NavigationState;
            }
        }

        return state;
    }

    private static PresenterNavigationState Dispatch(
        PresenterNavigationState state,
        UiActionId actionId,
        PresenterNavigationLayout layout)
    {
        return PresenterNavigationDispatch.Dispatch(
            state,
            actionId,
            Model,
            ProofOptions,
            layout);
    }

    private static PresenterNavigationLayout CompactLayout()
    {
        return PresenterNavigationLayout.Create(720, 760, PresenterShellMode.Compact);
    }

    private static PresenterNavigationState OblivionDocsState(string? selectedCardId = null)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs");
        if (!string.IsNullOrWhiteSpace(selectedCardId))
        {
            state = state.WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, selectedCardId);
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

    private static PresenterInputEvent KeyDown(PresenterKey key, bool ctrl = false)
    {
        return new PresenterInputEvent(
            PresenterInputKind.KeyDown,
            new PresenterInputPoint(float.NaN, float.NaN),
            PresenterInputButton.None,
            BackendName: "Test",
            Keyboard: new PresenterKeyboardInput(
                key,
                null,
                new PresenterKeyModifiers(ctrl, false, false, false),
                false));
    }

    private static PresenterInputEvent TextInput(string text)
    {
        return new PresenterInputEvent(
            PresenterInputKind.TextInput,
            new PresenterInputPoint(float.NaN, float.NaN),
            PresenterInputButton.None,
            BackendName: "Test",
            Keyboard: new PresenterKeyboardInput(
                PresenterKey.Unknown,
                text,
                PresenterKeyModifiers.None,
                false));
    }

    private static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static PresenterInputPoint ToRootPoint(
        PresenterNavigationShellRenderResult render,
        Rect rect)
    {
        return new PresenterInputPoint(
            (float)(render.Layout.ViewportRect.X + rect.X + (rect.Width / 2)),
            (float)(render.Layout.ViewportRect.Y + rect.Y + (rect.Height / 2)));
    }

    private static string PageText(PresenterPageRenderResult page)
    {
        return string.Join(
            Environment.NewLine,
            page.Frame.RenderCommands
                .OfType<DrawTextCommand>()
                .Select(command => command.Text));
    }

    private static string ShellText(PresenterNavigationShellRenderResult render)
    {
        return string.Join(
            Environment.NewLine,
            render.ShellFrame.RenderCommands
                .OfType<DrawTextCommand>()
                .Select(command => command.Text));
    }

    private static string ReadSampleSource()
    {
        return string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(RepoRoot, "samples", "Machina.Presenter.Sample"), "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m12h-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
