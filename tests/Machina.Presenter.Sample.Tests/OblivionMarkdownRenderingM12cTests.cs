using Copeland.Markdown;
using Machina.Dominatus.Rendering.Commands;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionMarkdownRenderingM12cTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();

    [Fact]
    public void MarkdownRenderer_RendersHeadingDistinctly()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Contains("H1 Markdown-first roadmap", OblivionMarkdownBody.BuildInspectorLines(card.Body), StringComparer.Ordinal);
    }

    [Fact]
    public void MarkdownRenderer_RendersParagraph()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Contains(
            OblivionMarkdownBody.BuildInspectorLines(card.Body),
            line => line.Contains("Oblivion treats Markdown as a", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownRenderer_RendersBulletList()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Contains(OblivionMarkdownBody.BuildInspectorLines(card.Body), line => line.StartsWith("\u2022 ", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownRenderer_RendersOrderedList_IfSupported()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Contains(OblivionMarkdownBody.BuildInspectorLines(card.Body), line => line.StartsWith("1. ", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownRenderer_RendersFencedCodeBlockAsStaticCode()
    {
        OblivionCard card = GetExecutionRoadmapCard("execution-deferred");

        Assert.Contains(OblivionMarkdownBody.BuildInspectorLines(card.Body), line => line == "code: csharp");
        Assert.Contains(OblivionMarkdownBody.BuildInspectorLines(card.Body), line => line.Contains("[Fact]", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownRenderer_RendersInlineCode()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("execution-deferred");
        string text = PageText(page);

        Assert.Contains("FactAttribute", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownRenderer_RendersStrongAndEmphasisOrFallback()
    {
        string text = string.Join(Environment.NewLine, OblivionMarkdownBody.BuildInspectorLines(GetExecutionRoadmapCard("markdown-first-roadmap").Body));

        Assert.Contains("text-card body language", text, StringComparison.Ordinal);
        Assert.Contains("`DocumentMir`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownRenderer_RendersLinkLabelAndTarget()
    {
        string text = string.Join(Environment.NewLine, OblivionMarkdownBody.BuildInspectorLines(GetExecutionRoadmapCard("selected-doc-dogfood").Body));

        Assert.Contains("frontend milestone doc", text, StringComparison.Ordinal);
        Assert.Contains("docs/copeland-markdown-frontend-m12a.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionCardRenderer_MarkdownPreviewUsesFirstHeading()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-first-roadmap");
        string text = PageText(page);

        Assert.Contains("Markdown-first roadmap", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionCardRenderer_MarkdownPreviewSummarizesParagraph()
    {
        OblivionCard card = GetExecutionRoadmapCard("selected-doc-dogfood");

        Assert.Contains(card.Body.PreviewLines, line => line.Contains("curated dogfood slice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OblivionCardRenderer_MarkdownPreviewSummarizesCodeFence()
    {
        OblivionCard card = GetExecutionRoadmapCard("execution-deferred");

        Assert.Contains("code: csharp", card.Body.PreviewLines, StringComparer.Ordinal);
    }

    [Fact]
    public void OblivionCardRenderer_MarkdownPreviewShowsDiagnosticsBadge()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-diagnostics-sample");
        string text = PageText(page);

        Assert.Contains("Diagnostics 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersMarkdownBodyWithHeadingsListsAndCode()
    {
        string text = string.Join(Environment.NewLine, OblivionMarkdownBody.BuildInspectorLines(GetExecutionRoadmapCard("markdown-first-roadmap").Body));

        Assert.Contains("H1", text, StringComparison.Ordinal);
        Assert.Contains("Workspace root remains JSON.", text, StringComparison.Ordinal);
        Assert.Contains("code: text", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersMarkdownDiagnosticsPanel()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-diagnostics-sample");
        string text = PageText(page);

        Assert.Contains("Markdown diagnostics", text, StringComparison.Ordinal);
        Assert.Contains(MarkdownDiagnosticIds.MalformedLink, text, StringComparison.Ordinal);
        Assert.Contains(MarkdownDiagnosticIds.UnclosedCodeFence, text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_ShowsMarkdownBodySourcePath()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Equal("body/markdown-first-roadmap.md", card.Body.BodySourcePath);
        Assert.Equal("cards/markdown-first-roadmap.card.toml", card.SourcePath);
    }

    [Fact]
    public void OblivionInspector_DoesNotExecuteCodeFence()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("execution-deferred");
        string text = PageText(page);

        Assert.Contains("Not executed in M11g.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Run fact ready", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownDiagnostics_RenderMalformedLink()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-diagnostics-sample");
        string diagnosticsText = string.Join(Environment.NewLine, OblivionMarkdownBody.BuildDiagnosticLines(card.Body.Diagnostics));

        Assert.Contains(MarkdownDiagnosticIds.MalformedLink, diagnosticsText, StringComparison.Ordinal);
        Assert.Contains("6:27", diagnosticsText, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownDiagnostics_RenderUnclosedFence()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-diagnostics-sample");
        string diagnosticsText = string.Join(Environment.NewLine, OblivionMarkdownBody.BuildDiagnosticLines(card.Body.Diagnostics));

        Assert.Contains(MarkdownDiagnosticIds.UnclosedCodeFence, diagnosticsText, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownDiagnostics_MalformedMarkdownDoesNotCrashPresenter()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-diagnostics-sample");

        Assert.NotEmpty(page.Frame.RenderCommands);
    }

    [Fact]
    public void OblivionMarkdownDogfood_LoadsSelectedDocMarkdown()
    {
        OblivionCard card = GetExecutionRoadmapCard("selected-doc-dogfood");

        Assert.Equal("body/selected-doc-dogfood.md", card.Body.BodySourcePath);
        Assert.NotNull(card.Body.DocumentMir);
    }

    [Fact]
    public void OblivionMarkdownDogfood_RendersSelectedDocInspector()
    {
        string text = string.Join(Environment.NewLine, OblivionMarkdownBody.BuildInspectorLines(GetExecutionRoadmapCard("selected-doc-dogfood").Body));

        Assert.Contains("curated dogfood slice", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rendering contract", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionMarkdownDogfood_ReportsUnsupportedSyntaxDeterministically()
    {
        string first = string.Join(Environment.NewLine, GetExecutionRoadmapCard("markdown-diagnostics-sample").Body.Diagnostics.Select(static diagnostic => diagnostic.ToString()));
        string second = string.Join(Environment.NewLine, GetExecutionRoadmapCard("markdown-diagnostics-sample").Body.Diagnostics.Select(static diagnostic => diagnostic.ToString()));

        Assert.Equal(first, second);
    }

    [Fact]
    public void M12c_DoesNotImplementMarkdownEditor()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("MarkdownEditor", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M12c_DoesNotImplementFileWatcher()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("FileSystemWatcher", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M12c_DoesNotImplementRoslynExecution()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.CodeAnalysis", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataReference", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M12c_DoesNotImplementVisionary()
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();

        Assert.DoesNotContain(model.Sections, section => string.Equals(section.Id, "visionary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M12c_DoesNotAddMarkdownParserDependency()
    {
        string repoRoot = GetRepositoryRoot();
        string[] bannedTerms = ["Markdig", "CommonMark", "MarkdownSharp", "MarkdownDeep"];

        IEnumerable<string> files = Directory
            .EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(repoRoot, "*.props", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(repoRoot, "*.targets", SearchOption.AllDirectories));

        string combinedText = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        foreach (string term in bannedTerms)
        {
            Assert.DoesNotContain($"PackageReference Include=\"{term}\"", combinedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExportPresenter_MarkdownRenderingCards_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-markdown-rendering-cards.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionMarkdownRenderingManifestJsonPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_MarkdownRenderingInspector_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-markdown-rendering-inspector-roadmap.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "execution-roadmap",
                    SelectedCardId: "markdown-first-roadmap"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionMarkdownRenderingManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M12cManifest_RecordsMarkdownRendering()
    {
        string manifestPath = ExportMarkdownManifest();
        string json = File.ReadAllText(manifestPath);

        Assert.Contains("\"milestone\": \"M12c\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"oblivion-markdown-rendering\"", json, StringComparison.Ordinal);
        Assert.Contains("\"documentMirRendered\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"diagnosticsRendered\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"editorImplemented\": false", json, StringComparison.Ordinal);
    }

    private static OblivionCard GetExecutionRoadmapCard(string cardId)
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(GetSampleWorkspacePath(), useCache: false);
        OblivionWorkspacePage page = Assert.Single(result.Workspace!.Sections.Single().Pages, candidate => candidate.Id == "execution-roadmap");
        return Assert.Single(page.Cards, card => card.Id.Value == cardId);
    }

    private static PresenterPageRenderResult RenderExecutionRoadmapPage(string selectedCardId)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "execution-roadmap")
            .WithSelectedCard(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, selectedCardId);

        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            DemoState.Default,
            StandardTheme.Default,
            new PresenterProofOptions(),
            PresenterNavigationLayout.Default.ContentVisibleWidth,
            state);
    }

    private static string RenderExecutionRoadmapText(string selectedCardId)
    {
        return PageText(RenderExecutionRoadmapPage(selectedCardId));
    }

    private static string PageText(PresenterPageRenderResult page)
    {
        return string.Join(
            Environment.NewLine,
            page.Frame.RenderCommands
                .OfType<DrawTextCommand>()
                .Select(command => command.Text));
    }

    private static string ExportMarkdownManifest()
    {
        string outputDirectory = CreateOutputDirectory();

        PresenterExportResult result = PresenterExporter.Export(
            DemoState.Default,
            Path.Combine(outputDirectory, "presenter-oblivion-markdown-manifest.png"),
            new PresenterProofOptions(),
            new PresenterNavigationExportOptions(
                true,
                SelectedSectionId: "oblivion",
                SelectedTabId: "execution-roadmap",
                SelectedCardId: "markdown-first-roadmap"),
            StandardTheme.Default);

        return result.OblivionMarkdownRenderingManifestJsonPath!;
    }

    private static string GetSampleWorkspacePath()
    {
        return Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "workspace.oblivion.json");
    }

    private static IEnumerable<string> GetSourceFiles(params string[] segments)
    {
        string[] pathParts = new string[segments.Length + 1];
        pathParts[0] = GetRepositoryRoot();
        Array.Copy(segments, 0, pathParts, 1, segments.Length);
        return Directory.GetFiles(Path.Combine(pathParts), "*.cs", SearchOption.AllDirectories);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string CreateOutputDirectory()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-m12c-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
