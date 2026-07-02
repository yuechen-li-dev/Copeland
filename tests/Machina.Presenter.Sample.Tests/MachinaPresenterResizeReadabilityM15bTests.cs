using System.Text.Json;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class MachinaPresenterResizeReadabilityM15bTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();
    private static readonly StandardTheme Theme = StandardTheme.Default;
    private static readonly ColorToken DarkPreviewFrame = ColorToken.Hex(0x0B1220FF);

    [Fact]
    public void PresenterSurfaceSize_Exact16x9_UsesFullSurface()
    {
        PresenterSurfaceSize surface = PresenterSurfaceSize.Compute(1280, 720);

        Assert.Equal(1280, surface.SurfaceWidth);
        Assert.Equal(720, surface.SurfaceHeight);
        Assert.Equal(0, surface.SurfaceX);
        Assert.Equal(0, surface.SurfaceY);
        Assert.False(surface.IsLetterboxed);
    }

    [Fact]
    public void PresenterSurfaceSize_WideWindow_ComputesCentered16x9Surface()
    {
        PresenterSurfaceSize surface = PresenterSurfaceSize.Compute(1600, 1000);

        Assert.Equal(1600, surface.SurfaceWidth);
        Assert.Equal(900, surface.SurfaceHeight);
        Assert.Equal(0, surface.SurfaceX);
        Assert.Equal(50, surface.SurfaceY);
        Assert.True(surface.IsLetterboxed);
    }

    [Fact]
    public void PresenterSurfaceSize_TallWindow_ComputesCentered16x9Surface()
    {
        PresenterSurfaceSize surface = PresenterSurfaceSize.Compute(1000, 1000);

        Assert.Equal(1000, surface.SurfaceWidth);
        Assert.Equal(562, surface.SurfaceHeight);
        Assert.Equal(0, surface.SurfaceX);
        Assert.Equal(219, surface.SurfaceY);
        Assert.True(surface.IsLetterboxed);
    }

    [Fact]
    public void PresenterSurfaceSize_ClampsToMinimumUsableSize()
    {
        PresenterSurfaceSize surface = PresenterSurfaceSize.Compute(800, 400);

        Assert.Equal(PresenterSurfaceSize.MinimumSurfaceWidth, surface.WindowWidth);
        Assert.Equal(PresenterSurfaceSize.MinimumSurfaceHeight, surface.WindowHeight);
        Assert.Equal(PresenterSurfaceSize.MinimumSurfaceWidth, surface.SurfaceWidth);
        Assert.Equal(PresenterSurfaceSize.MinimumSurfaceHeight, surface.SurfaceHeight);
    }

    [Fact]
    public void PresenterSurfaceSize_DefaultRuntimeSurface_Is16x9()
    {
        PresenterSurfaceSize surface = PresenterSurfaceSize.DefaultRuntime;

        Assert.Equal(PresenterSurfaceSize.DefaultRuntimeSurfaceWidth, surface.SurfaceWidth);
        Assert.Equal(PresenterSurfaceSize.DefaultRuntimeSurfaceHeight, surface.SurfaceHeight);
        Assert.Equal(0, surface.SurfaceX);
        Assert.Equal(0, surface.SurfaceY);
    }

    [Fact]
    public void RuntimePresenter_DefaultSize_Is16x9()
    {
        Assert.Equal(1280, PresenterSurfaceSize.DefaultRuntime.SurfaceWidth);
        Assert.Equal(720, PresenterSurfaceSize.DefaultRuntime.SurfaceHeight);
    }

    [Fact]
    public void RuntimePresenter_DoesNotUseExportFrameSizeAsResizeClamp()
    {
        Assert.Equal(1120, PresenterNavigationExportOptions.DefaultShell.Width);
        Assert.Equal(760, PresenterNavigationExportOptions.DefaultShell.Height);
        Assert.NotEqual(PresenterNavigationExportOptions.DefaultShell.Width, PresenterSurfaceSize.DefaultRuntime.SurfaceWidth);
        Assert.NotEqual(PresenterNavigationExportOptions.DefaultShell.Height, PresenterSurfaceSize.DefaultRuntime.SurfaceHeight);
    }

    [Fact]
    public void ExportPresenter_StillAcceptsExplicitWidthAndHeight()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "m15b-explicit-size.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    Width: 1600,
                    Height: 900),
                Theme);

            Assert.Equal(1600, result.Width);
            Assert.Equal(900, result.Height);
            Assert.True(File.Exists(result.OutputPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void PresenterResize_RebuildsLayoutForEffectiveSurface()
    {
        PresenterNavigationRenderSession session = new();
        PresenterNavigationState state = CreateDocsState();

        PresenterNavigationShellRenderResult compact = RenderShell(session, state, 960, 540);
        PresenterNavigationShellRenderResult wide = RenderShell(session, state, 1280, 720);

        Assert.Equal(2, wide.Diagnostics.PageRenderCount);
        Assert.Equal(2, wide.Diagnostics.ShellRenderCount);
        Assert.NotEqual(compact.Layout.RootWidth, wide.Layout.RootWidth);
        Assert.NotEqual(compact.Layout.ContentVisibleWidth, wide.Layout.ContentVisibleWidth);
    }

    [Fact]
    public void PresenterResize_ChangingWidthCanChangeShellMode()
    {
        PresenterNavigationShellRenderResult compact = RenderShell(session: null, CreateDocsState(), 960, 540);
        PresenterNavigationShellRenderResult wide = RenderShell(session: null, CreateDocsState(), 1280, 720);

        Assert.Equal(PresenterShellMode.Compact, compact.ShellMode);
        Assert.Equal(PresenterShellMode.Wide, wide.ShellMode);
    }

    [Fact]
    public void PresenterResize_SelectedCardSurvivesResize()
    {
        PresenterNavigationState state = CreateDocsState("doc-copeland-markdown-frontend-m12a");

        PresenterNavigationShellRenderResult compact = RenderShell(session: null, state, 960, 540);
        PresenterNavigationShellRenderResult wide = RenderShell(session: null, state, 1280, 720);

        Assert.Equal(
            compact.NavigationState.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)),
            wide.NavigationState.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId)));
    }

    [Fact]
    public void PresenterResize_ScrollOffsetsRemainDeterministic()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("components")
            .WithSelectedTab("components", "controls")
            .WithScrollOffset("components.controls", 4096);

        PresenterNavigationShellRenderResult compact = RenderShell(session: null, state, 960, 540);
        PresenterNavigationShellRenderResult wide = RenderShell(session: null, state, 1280, 720);

        Assert.Equal(compact.ScrollbarGeometry.MaxScrollOffset, compact.NavigationState.GetScrollOffset("components.controls"));
        Assert.Equal(wide.ScrollbarGeometry.MaxScrollOffset, wide.NavigationState.GetScrollOffset("components.controls"));
    }

    [Fact]
    public void OblivionCardPreview_BodyTextIsReadable()
    {
        PresenterPageRenderResult page = RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            1280,
            CreateDocsState("doc-aurelian-build-topology-m13b"));
        DrawTextCommand[] previewCommands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command =>
                command.Id.Contains("doc-aurelian-build-topology-m13b", StringComparison.Ordinal) &&
                command.Id.Contains(".body-line-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(previewCommands);
        Assert.Contains(previewCommands, command => command.Text.Trim().Length >= 12);
        Assert.All(previewCommands, command => Assert.NotEqual(DarkPreviewFrame, command.Style.Color));
    }

    [Fact]
    public void OblivionCardPreview_MarkdownSummaryUsesReadableContrast()
    {
        PresenterPageRenderResult page = RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            1280,
            CreateDocsState("doc-aurelian-build-topology-m13b"));
        DrawTextCommand summary = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .First(command =>
                command.Id.Contains("doc-aurelian-build-topology-m13b", StringComparison.Ordinal) &&
                command.Id.Contains(".body-line-", StringComparison.Ordinal));
        string cardId = "doc-aurelian-build-topology-m13b";

        FillRectCommand frame = Assert.Single(
            page.Frame.RenderCommands.OfType<FillRectCommand>(),
            command =>
                command.Id.Contains(cardId, StringComparison.Ordinal) &&
                command.Id.EndsWith(".body-frame", StringComparison.Ordinal));

        Assert.Equal(DarkPreviewFrame, frame.Color);
        Assert.NotEqual(frame.Color, summary.Style.Color);
        Assert.NotEqual(ColorToken.Hex(0x111827FF), summary.Style.Color);
    }

    [Fact]
    public void OblivionCardPreview_BodyTextWrapsOrElides()
    {
        PresenterPageRenderResult page = RenderPage(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            960,
            CreateExecutionRoadmapState("selected-doc-dogfood"));
        DrawTextCommand[] previewLines = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command =>
                command.Id.Contains(".preview-", StringComparison.Ordinal) ||
                command.Id.Contains(".plain-", StringComparison.Ordinal) ||
                command.Id.Contains(".body-line-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(previewLines);
        Assert.True(
            previewLines.Any(command => command.Text.EndsWith("...", StringComparison.Ordinal)) ||
            previewLines.GroupBy(command => command.Id[..command.Id.LastIndexOf('-')])
                .Any(group => group.Count() >= 2));
    }

    [Fact]
    public void OblivionCardPreview_TextDoesNotOverflowCardBody()
    {
        PresenterPageRenderResult page = RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            1280,
            CreateDocsState("doc-aurelian-build-topology-m13b"));
        PresenterCardFrame frame = OblivionCardRenderer.DescribeFrame(page.Frame.Resolved, "doc-aurelian-build-topology-m13b");
        DrawTextCommand[] previewCommands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command =>
                command.Id.Contains("doc-aurelian-build-topology-m13b", StringComparison.Ordinal) &&
                command.Id.Contains(".body-line-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(previewCommands);
        Assert.All(previewCommands, command => AssertRectInside(command.Rect, frame.ContentBounds, command.Id));
    }

    [Fact]
    public void OblivionCardPreview_DoesNotRenderDarkTextOnDarkFrame()
    {
        PresenterPageRenderResult page = RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            1280,
            CreateDocsState("doc-aurelian-build-topology-m13b"));
        DrawTextCommand[] previewCommands = page.Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command =>
                command.Id.Contains("doc-aurelian-build-topology-m13b", StringComparison.Ordinal) &&
                command.Id.Contains(".body-line-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(previewCommands);
        Assert.DoesNotContain(previewCommands, command => command.Style.Color == DarkPreviewFrame);
        Assert.DoesNotContain(previewCommands, command => command.Style.Color == ColorToken.Hex(0x000000FF));
    }

    [Fact]
    public void OblivionInspector_StillRendersSelectedCard()
    {
        PresenterNavigationShellRenderResult render = RenderShell(session: null, CreateDocsState("doc-copeland-markdown-frontend-m12a"), 1280, 720);
        string text = string.Join(
            Environment.NewLine,
            render.PageFrame.RenderCommands
                .OfType<DrawTextCommand>()
                .Select(command => command.Text));

        Assert.Contains("Selected card inspector", text, StringComparison.Ordinal);
        Assert.Contains("Copeland Markdown Frontend", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_MetadataStillReadable()
    {
        PresenterNavigationShellRenderResult render = RenderShell(session: null, CreateDocsState("doc-copeland-markdown-frontend-m12a"), 1280, 720);
        DrawTextCommand[] metadata = render.PageFrame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command =>
                command.Id.Contains("oblivion.docs.metadata", StringComparison.Ordinal) ||
                command.Text.Contains("Owner", StringComparison.Ordinal) ||
                command.Text.Contains("Status", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(metadata);
        Assert.All(metadata, command => Assert.NotEqual(DarkPreviewFrame, command.Style.Color));
    }

    [Fact]
    public void M15bManifest_RecordsResizable16x9Surface()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.Equal("M15b", root.GetProperty("milestone").GetString());
        Assert.Equal("presenter-16x9-resize-readable-card-previews", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("runtimeWindowResizable").GetBoolean());
        Assert.Equal("16:9", root.GetProperty("presenterSurfaceAspectRatio").GetString());
        Assert.True(root.GetProperty("runtimeExportSizingSeparated").GetBoolean());
        Assert.True(root.GetProperty("liveLayoutRecomposition").GetBoolean());
        Assert.True(root.GetProperty("adaptiveShellUsesLiveEffectiveWidth").GetBoolean());
    }

    [Fact]
    public void M15bManifest_RecordsReadablePreviews()
    {
        using JsonDocument manifest = LoadManifest();
        JsonElement root = manifest.RootElement;

        Assert.True(root.GetProperty("cardPreviewReadable").GetBoolean());
        Assert.True(root.GetProperty("cardPreviewWrapOrElide").GetBoolean());
        Assert.True(root.GetProperty("contrastFixesApplied").GetBoolean());
        Assert.False(root.GetProperty("inspectorRegressed").GetBoolean());
    }

    [Fact]
    public void M15bExportArtifacts_AreWritten()
    {
        string[] expectedArtifacts =
        [
            "artifacts/m15b/m15b-oblivion-cards-960x540.png",
            "artifacts/m15b/m15b-oblivion-cards-1280x720.png",
            "artifacts/m15b/m15b-oblivion-docs-1280x720.png",
            "artifacts/m15b/m15b-oblivion-docs-1600x900.png",
            "artifacts/m15b/m15b-oblivion-docs-compact-960x540.png",
            "artifacts/m15b/m15b-oblivion-inspector-1280x720.png",
        ];

        Assert.All(
            expectedArtifacts,
            relativePath => Assert.True(File.Exists(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))));
    }

    [Fact]
    public void M15b_DoesNotImplementEditor()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("editorImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M15b_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("roslynExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M15b_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M15b_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("arbitrary2DLayoutSolverImplemented").GetBoolean());
    }

    private static PresenterNavigationShellRenderResult RenderShell(
        PresenterNavigationRenderSession? session,
        PresenterNavigationState state,
        int width,
        int height)
    {
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state,
            Theme,
            ProofOptions,
            session,
            layout);
    }

    private static PresenterPageRenderResult RenderPage(
        string pageId,
        int contentWidth,
        PresenterNavigationState? state = null)
    {
        PresenterNavigationState effectiveState = state ?? (pageId == OblivionWorkbenchCatalog.DocsPageId
            ? CreateDocsState("doc-copeland-markdown-frontend-m12a")
            : PresenterNavigationState.CreateDefault(Model)
                .WithSelectedSection("oblivion")
                .WithSelectedTab("oblivion", "cards"));

        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(contentWidth);
        return PresenterNavigationCatalog.RenderPage(
            pageId,
            DemoState.Default,
            Theme,
            ProofOptions,
            PresenterNavigationLayout.Create(contentWidth, 720, shellMode).ContentVisibleWidth,
            effectiveState,
            shellMode);
    }

    private static PresenterNavigationState CreateDocsState(string? selectedCardId = null)
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

    private static PresenterNavigationState CreateExecutionRoadmapState(string? selectedCardId = null)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "execution-roadmap");

        if (!string.IsNullOrWhiteSpace(selectedCardId))
        {
            state = state.WithSelectedCard(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, selectedCardId);
        }

        return state;
    }

    private static JsonDocument LoadManifest()
    {
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                RepoRoot,
                "artifacts",
                "m15b",
                "machina-presenter-resize-readability-manifest.json")));
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
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m15b-tests", Guid.NewGuid().ToString("N"));
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
