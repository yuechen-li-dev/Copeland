using System.Text.Json;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionExpandedMarkdownReadingSurfaceM15dTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();

    [Fact]
    public void MarkdownReadingStyle_DefaultExpandedSurfaceHasReadableContrast()
    {
        OblivionMarkdownReadingStyle style = OblivionMarkdownReadingStyle.Default;

        Assert.NotEqual(style.Surface, style.Foreground);
        Assert.True(Math.Abs(GetRelativeLuminance(style.Surface) - GetRelativeLuminance(style.Foreground)) > 0.4);
    }

    [Fact]
    public void MarkdownReadingStyle_IsImmutableRecord()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot, "samples", "Machina.UI", "Machina.Presenter.Sample", "OblivionMarkdownReadingStyle.cs"));

        Assert.Contains("public sealed record OblivionMarkdownReadingStyle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandedMarkdownRenderer_UsesReadingStyleForeground()
    {
        DrawTextCommand[] commands = RenderDocsPage(expandedCardId: ExpandedDocCardId).Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(commands, command => command.Style.Color == OblivionCardRenderer.MarkdownReadingStyle.Foreground);
    }

    [Fact]
    public void ExpandedMarkdownRenderer_DoesNotRenderDarkTextOnDarkSurface()
    {
        DrawTextCommand[] commands = RenderDocsPage(expandedCardId: ExpandedDocCardId).Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains($"{ExpandedDocCardId}.expanded.block-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(commands);
        Assert.DoesNotContain(commands, command => command.Style.Color == OblivionCardRenderer.MarkdownReadingStyle.Surface);
        Assert.DoesNotContain(commands, command => command.Style.Color == ColorToken.Hex(0x000000FF));
    }

    [Fact]
    public void ExpandedMarkdownRenderer_CodeBlockUsesReadableContrast()
    {
        OblivionMarkdownReadingStyle style = OblivionCardRenderer.MarkdownReadingStyle;

        Assert.NotEqual(style.CodeSurface, style.CodeForeground);
        Assert.True(Math.Abs(GetRelativeLuminance(style.CodeSurface) - GetRelativeLuminance(style.CodeForeground)) > 0.35);
    }

    [Fact]
    public void ExpandedMarkdownCard_UsesDocumentScaleHeight()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), 1280, 720);
        PresenterCardFrame frame = OblivionCardRenderer.DescribeFrame(render.PageRender!.Frame.Resolved, ExpandedDocCardId);

        Assert.True(frame.Bounds.Height >= render.Layout.ViewportHeight - 28);
    }

    [Fact]
    public void ExpandedMarkdownCard_BodyViewportGetsMostAvailableHeight()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), 1280, 720);
        OblivionCompactCardView view = GetBuiltDocCard(expanded: true).CompactView;
        OblivionExpandedBodyViewport? viewport = OblivionCardRenderer.DescribeExpandedBodyViewport(render.PageRender!.Frame.Resolved, view, ExpandedDocCardId);

        Assert.NotNull(viewport);
        Assert.True(viewport!.Bounds.Height >= render.Layout.ViewportHeight * 0.55);
    }

    [Fact]
    public void ExpandedMarkdownCard_CollapsedCardsRemainCompact()
    {
        OblivionBuiltCard collapsed = GetBuiltDocCard(expanded: false);
        OblivionBuiltCard expanded = GetBuiltDocCard(expanded: true);

        Assert.True(collapsed.CompactView.PreferredHeight < expanded.CompactView.ExpandedPreferredHeight);
    }

    [Fact]
    public void ExpandedMarkdownCard_LongBodyStillUsesLocalScroll()
    {
        PresenterNavigationShellRenderResult render = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), 1280, 720);
        OblivionCardBodyHitTarget bodyTarget = Assert.Single(
            render.PageRender!.OblivionInteraction!.BodyTargets,
            target => string.Equals(target.CardId, ExpandedDocCardId, StringComparison.Ordinal));

        Assert.True(bodyTarget.ScrollbarGeometry.IsVisible);
        Assert.True(bodyTarget.ScrollbarGeometry.MaxScrollOffset > 0);
    }

    [Fact]
    public void ExpandedMarkdownCard_ExpandingOneCollapsesOtherMarkdownCards()
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: ExpandedDocCardId);

        PresenterNavigationState next = Dispatch(
            state,
            PresenterNavigationActions.ToggleOblivionCardExpansion(OblivionWorkbenchCatalog.DocsPageId, SecondaryMarkdownDocCardId));

        Assert.False(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
        Assert.True(next.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, SecondaryMarkdownDocCardId).IsExpanded);
    }

    [Fact]
    public void ExpandedMarkdownCard_SelectedCardRemainsSelectedAfterExpansion()
    {
        PresenterNavigationState next = Dispatch(
            CreateDocsState(),
            PresenterNavigationActions.ToggleOblivionCardExpansion(OblivionWorkbenchCatalog.DocsPageId, SecondaryMarkdownDocCardId));

        Assert.Equal(
            SecondaryMarkdownDocCardId,
            next.GetSelectedCardId(OblivionWorkbenchCatalog.DocsPageId, GetDocsCards()));
    }

    [Fact]
    public void OblivionInspector_DoesNotRenderFormattedMarkdownBody()
    {
        DrawTextCommand[] commands = RenderDocsPage(
                expandedCardId: ExpandedDocCardId,
                inspectorScrollOffset: 240)
            .Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .ToArray();

        Assert.DoesNotContain(commands, command => command.Id.Contains(".markdown.block-", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Id.Contains(".wide-inspector-raw-source.source-line-", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionInspector_ShowsRawMarkdownSource()
    {
        string firstSourceLine = GetDocsCards()
            .First(card => string.Equals(card.Id.Value, ExpandedDocCardId, StringComparison.Ordinal))
            .Body.RawText!
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .First(line => !string.IsNullOrWhiteSpace(line));

        string text = PageText(RenderDocsPage(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240));

        Assert.Contains(firstSourceLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RawMarkdownSourceIsScrollable()
    {
        FillRectCommand[] commands = RenderDocsPage(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240).Frame.RenderCommands
            .OfType<FillRectCommand>()
            .ToArray();

        Assert.Contains(commands, command => command.Id.Contains(".wide-inspector-raw-source.scrollbar-track", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Id.Contains(".wide-inspector-raw-source.scrollbar-thumb", StringComparison.Ordinal));
    }

    [Fact]
    public void OblivionInspector_RawMarkdownUsesReadableCodeStyle()
    {
        DrawTextCommand[] commands = RenderDocsPage(expandedCardId: ExpandedDocCardId, inspectorScrollOffset: 240).Frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.Contains(".wide-inspector-raw-source.source-line-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.Equal(OblivionCardRenderer.MarkdownReadingStyle.SourceForeground, command.Style.Color));
    }

    [Fact]
    public void OblivionInspector_StillShowsMetadataActionsDiagnosticsArtifacts()
    {
        string text = PageText(RenderDocsPage(expandedCardId: ExpandedDocCardId));

        Assert.Contains("Metadata", text, StringComparison.Ordinal);
        Assert.Contains("Markdown diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("Available actions", text, StringComparison.Ordinal);
        Assert.Contains("Artifacts metadata", text, StringComparison.Ordinal);
    }

    [Fact]
    public void M15d_PreservesM15cExpansionToggle()
    {
        PresenterNavigationState expanded = Dispatch(
            CreateDocsState(),
            PresenterNavigationActions.ToggleOblivionCardExpansion(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId));
        PresenterNavigationState collapsed = Dispatch(
            expanded,
            PresenterNavigationActions.ToggleOblivionCardExpansion(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId));

        Assert.True(expanded.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
        Assert.False(collapsed.GetCardViewState(OblivionWorkbenchCatalog.DocsPageId, ExpandedDocCardId).IsExpanded);
    }

    [Fact]
    public void M15d_PreservesM15bResizeBehavior()
    {
        PresenterNavigationShellRenderResult compact = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), 960, 540);
        PresenterNavigationShellRenderResult wide = RenderShell(CreateDocsState(expandedCardId: ExpandedDocCardId), 1280, 720);

        Assert.Equal(PresenterShellMode.Compact, compact.ShellMode);
        Assert.Equal(PresenterShellMode.Wide, wide.ShellMode);
        Assert.True(wide.Layout.ViewportHeight > compact.Layout.ViewportHeight);
    }

    [Fact]
    public void M15d_DoesNotImplementMarkdownEditing()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("markdownEditingImplemented").GetBoolean());
    }

    [Fact]
    public void M15d_DoesNotImplementNotebookExecution()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("notebookExecutionImplemented").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("roslynExecutionImplemented").GetBoolean());
    }

    [Fact]
    public void M15d_DoesNotPerformAurelianWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("aurelianWorkPerformed").GetBoolean());
    }

    [Fact]
    public void M15d_DoesNotPerformVdMirWork()
    {
        using JsonDocument manifest = LoadManifest();
        Assert.False(manifest.RootElement.GetProperty("vdMirWorkPerformed").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("arbitrary2DLayoutSolverImplemented").GetBoolean());
    }

    [Fact]
    public void M15dManifest_RecordsReadingSurfaceFixes()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WriteExpandedMarkdownReadingSurfaceManifest(outputDirectory, CreateDocsState(expandedCardId: ExpandedDocCardId));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.True(File.Exists(textPath));
            Assert.True(document.RootElement.GetProperty("expandedMarkdownReadableContrast").GetBoolean());
            Assert.True(document.RootElement.GetProperty("markdownReadingStyleRecordImplemented").GetBoolean());
            Assert.True(document.RootElement.GetProperty("expandedCardUsesDocumentHeight").GetBoolean());
            Assert.False(document.RootElement.GetProperty("inspectorRendersMarkdownBody").GetBoolean());
            Assert.True(document.RootElement.GetProperty("inspectorShowsRawMarkdownSource").GetBoolean());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M15dExportArtifacts_AreWritten()
    {
        string[] expectedArtifacts =
        [
            "artifacts/m15d/m15d-oblivion-expanded-dark-readable-1280x720.png",
            "artifacts/m15d/m15d-oblivion-expanded-full-height-1280x720.png",
            "artifacts/m15d/m15d-oblivion-expanded-scrolled-1280x720.png",
            "artifacts/m15d/m15d-oblivion-inspector-raw-markdown-1280x720.png",
            "artifacts/m15d/m15d-oblivion-docs-compact-expanded-960x540.png",
            "artifacts/m15d/m15d-oblivion-cards-expanded-1280x720.png",
            "artifacts/m15d/oblivion-expanded-markdown-reading-surface-manifest.json",
            "artifacts/m15d/oblivion-expanded-markdown-reading-surface-manifest.txt",
        ];

        Assert.All(
            expectedArtifacts,
            relativePath => Assert.True(File.Exists(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))));
    }

    private static IReadOnlyList<OblivionCard> GetDocsCards()
    {
        return OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId);
    }

    private static OblivionBuiltCard GetBuiltDocCard(bool expanded)
    {
        PresenterNavigationState state = CreateDocsState(expandedCardId: expanded ? ExpandedDocCardId : null);
        return Assert.Single(
            OblivionWorkbenchCatalog.GetBuiltPageCardsForSelection(
                OblivionWorkbenchCatalog.DocsPageId,
                ProofOptions,
                state.EffectState,
                state),
            card => string.Equals(card.SourceCard.Id.Value, ExpandedDocCardId, StringComparison.Ordinal));
    }

    private static PresenterNavigationState CreateDocsState(
        string? selectedCardId = null,
        string? expandedCardId = null,
        double bodyScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs");

        if (!string.IsNullOrWhiteSpace(selectedCardId))
        {
            state = state.WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, selectedCardId);
        }

        if (!string.IsNullOrWhiteSpace(selectedCardId))
        {
            state = state
                .WithInspectorScrollOffset(OblivionWorkbenchCatalog.DocsPageId, inspectorScrollOffset)
                .WithRawMarkdownSourceScrollOffset(selectedCardId, rawSourceScrollOffset);
        }

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state
                .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, expandedCardId)
                .WithCardViewState(
                    OblivionWorkbenchCatalog.DocsPageId,
                    expandedCardId,
                    new OblivionCardViewState(true, bodyScrollOffset));
        }

        return state;
    }

    private static PresenterPageRenderResult RenderDocsPage(
        string? expandedCardId = null,
        double bodyScrollOffset = 0,
        double inspectorScrollOffset = 0,
        double rawSourceScrollOffset = 0)
    {
        PresenterNavigationState state = CreateDocsState(
            selectedCardId: expandedCardId ?? ExpandedDocCardId,
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

    private static PresenterPageRenderResult RenderExecutionRoadmapPage(string selectedCardId, string? expandedCardId = null)
    {
        return RenderExecutionRoadmapPage(selectedCardId, expandedCardId, bodyScrollOffset: 0);
    }

    private static PresenterPageRenderResult RenderExecutionRoadmapPage(string selectedCardId, string? expandedCardId, double bodyScrollOffset)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "execution-roadmap")
            .WithSelectedCard(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, selectedCardId);

        if (!string.IsNullOrWhiteSpace(expandedCardId))
        {
            state = state.WithCardViewState(
                OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
                expandedCardId,
                new OblivionCardViewState(true, bodyScrollOffset));
        }

        int width = 1280;
        int height = 720;
        PresenterShellMode shellMode = PresenterShellModeResolver.Resolve(width);
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(width, height, shellMode);
        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            DemoState.Default,
            StandardTheme.Default,
            ProofOptions,
            layout.ContentVisibleWidth,
            layout.ViewportHeight,
            state,
            shellMode);
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

    private static PresenterNavigationState Dispatch(PresenterNavigationState state, Machina.Core.Actions.UiActionId actionId)
    {
        PresenterNavigationLayout layout = PresenterNavigationLayout.Create(1280, 720, PresenterShellMode.Wide);
        return PresenterNavigationDispatch.Dispatch(state, actionId, Model, ProofOptions, layout);
    }

    private static string PageText(PresenterPageRenderResult page)
    {
        return string.Join(
            Environment.NewLine,
            page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text));
    }

    private static JsonDocument LoadManifest()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WriteExpandedMarkdownReadingSurfaceManifest(outputDirectory, CreateDocsState(expandedCardId: ExpandedDocCardId));
            return JsonDocument.Parse(File.ReadAllText(jsonPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    private static double GetRelativeLuminance(ColorToken color)
    {
        byte red = (byte)(color.Rgba >> 24);
        byte green = (byte)(color.Rgba >> 16);
        byte blue = (byte)(color.Rgba >> 8);
        return (0.2126 * NormalizeChannel(red)) + (0.7152 * NormalizeChannel(green)) + (0.0722 * NormalizeChannel(blue));
    }

    private static double NormalizeChannel(byte channel)
    {
        double normalized = channel / 255.0;
        return normalized <= 0.03928
            ? normalized / 12.92
            : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m15d-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private const string ExpandedDocCardId = "doc-aurelian-build-topology-m13b";
    private const string SecondaryMarkdownDocCardId = "doc-copeland-markdown-frontend-m12a";

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
