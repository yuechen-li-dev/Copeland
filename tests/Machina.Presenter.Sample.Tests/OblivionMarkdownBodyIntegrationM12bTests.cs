using Copeland.Markdown;
using Machina.Dominatus.Rendering.Commands;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionMarkdownBodyIntegrationM12bTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();

    [Fact]
    public void OblivionCardToml_LoadsCopelandMarkdownBodyPath()
    {
        string path = GetSampleCardPath("markdown-first-roadmap.card.toml");
        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(File.ReadAllText(path), path);

        Assert.NotNull(result.Document);
        Assert.Equal("copeland-markdown", result.Document!.Body.Format);
        Assert.Equal("body/markdown-first-roadmap.md", result.Document.Body.Path);
        Assert.Null(result.Document.Body.Text);
    }

    [Fact]
    public void OblivionCardToml_RejectsAbsoluteMarkdownBodyPath()
    {
        string toml = """
            format = 1
            kind = "card"
            id = "absolute-markdown"
            card_kind = "note"
            status = "idle"
            title = "Absolute markdown"

            [body]
            format = "copeland-markdown"
            path = "C:/outside.md"
            """;

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml, "cards/absolute.card.toml");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "absolute-path-not-allowed");
    }

    [Fact]
    public void OblivionCardToml_RejectsMarkdownBodyPathTraversal()
    {
        string toml = """
            format = 1
            kind = "card"
            id = "traversal-markdown"
            card_kind = "note"
            status = "idle"
            title = "Traversal markdown"

            [body]
            format = "copeland-markdown"
            path = "../outside.md"
            """;

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml, "cards/traversal.card.toml");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "path-traversal-not-allowed");
    }

    [Fact]
    public void OblivionWorkspaceLoader_LoadsMarkdownBodyFile()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Equal(OblivionCardBodyFormat.CopelandMarkdown, card.Body.Format);
        Assert.Equal("body/markdown-first-roadmap.md", card.Body.BodySourcePath);
        Assert.NotNull(card.Body.RawText);
    }

    [Fact]
    public void OblivionWorkspaceLoader_ReportsMissingMarkdownBodyFile()
    {
        string directory = CreateWorkspaceDirectory();

        try
        {
            WriteWorkspaceWithMarkdownBody(directory, "body/missing.md", markdownFileContent: null);

            OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(Path.Combine(directory, "workspace.oblivion.json"), useCache: false);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "missing-markdown-body-file");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void OblivionWorkspaceLoader_CompilesMarkdownBodyToDocumentMir()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        DocumentMir mir = Assert.IsType<DocumentMir>(card.Body.DocumentMir);
        Assert.Contains(mir.Blocks, block => block is HeadingMir);
        Assert.Contains(mir.Blocks, block => block is ListMir);
    }

    [Fact]
    public void OblivionWorkspaceLoader_PreservesMarkdownDiagnostics()
    {
        OblivionCard card = GetExecutionRoadmapCard("markdown-readiness-audit");

        Assert.NotEmpty(card.Body.Diagnostics);
        Assert.Contains(card.Body.Diagnostics, diagnostic => diagnostic.Code == MarkdownDiagnosticIds.MalformedLink);
    }

    [Fact]
    public void OblivionWorkspaceLoader_MalformedMarkdownDoesNotCrash()
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(GetSampleWorkspacePath(), useCache: false);

        Assert.NotNull(result.Workspace);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == MarkdownDiagnosticIds.MalformedLink);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "missing-workspace-manifest");
    }

    [Fact]
    public void OblivionWorkspaceLoader_CodeFenceIsStaticTextOnly()
    {
        OblivionCard card = GetExecutionRoadmapCard("execution-deferred");
        CodeBlockMir codeBlock = Assert.IsType<CodeBlockMir>(Assert.Single(card.Body.DocumentMir!.Blocks.OfType<CodeBlockMir>()));

        Assert.Contains("[Fact]", codeBlock.Text, StringComparison.Ordinal);
        Assert.All(card.Actions, action => Assert.False(action.Enabled));
    }

    [Fact]
    public void OblivionCardRenderer_RendersMarkdownPreview()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-first-roadmap");
        string text = PageText(page);

        Assert.Contains("Markdown body", text, StringComparison.Ordinal);
        Assert.Contains("Workspace root remains JSON.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersMarkdownHeadingParagraphList()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-first-roadmap");
        string text = PageText(page);

        Assert.Contains("# Markdown-first roadmap", text, StringComparison.Ordinal);
        Assert.Contains("Oblivion treats Markdown as a **text-card body", text, StringComparison.Ordinal);
        Assert.Contains("Markdown cards come first.", text, StringComparison.Ordinal);
        Assert.Contains("- Workspace root remains JSON.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersMarkdownCodeFence()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("execution-deferred");
        string text = PageText(page);

        Assert.Contains("```csharp", text, StringComparison.Ordinal);
        Assert.Contains("[Fact]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersMarkdownDiagnostics()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-readiness-audit");
        string text = PageText(page);

        Assert.Contains("Markdown diagnostics", text, StringComparison.Ordinal);
        Assert.Contains(MarkdownDiagnosticIds.MalformedLink, text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_DoesNotTreatMarkdownAsWholePage()
    {
        PresenterPageRenderResult page = RenderExecutionRoadmapPage("markdown-first-roadmap");
        string text = PageText(page);

        Assert.Contains("Execution deferred", text, StringComparison.Ordinal);
        Assert.Contains("Visionary future card", text, StringComparison.Ordinal);
        Assert.Contains("Body format: Copeland Markdown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionDocumentModel_RemainsStackOfTypedCards()
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(GetSampleWorkspacePath(), useCache: false);
        OblivionWorkspacePage page = Assert.Single(result.Workspace!.Sections.Single().Pages, candidate => candidate.Id == "execution-roadmap");

        Assert.Equal(5, page.Cards.Count);
        Assert.All(page.Cards, card => Assert.IsType<OblivionCard>(card));
    }

    [Fact]
    public void MarkdownBody_DoesNotCreateImageTableVideoCards()
    {
        string cardKinds = string.Join(",", OblivionWorkbenchCatalog.CreateAllCards().Select(card => card.Kind.ToString()));

        Assert.DoesNotContain("Image", cardKinds, StringComparison.Ordinal);
        Assert.DoesNotContain("Table", cardKinds, StringComparison.Ordinal);
        Assert.DoesNotContain("Video", cardKinds, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleFileMarkdownExport_IsDeferred()
    {
        string manifestPath = ExportMarkdownManifest();
        string json = File.ReadAllText(manifestPath);

        Assert.Contains("\"singleFileMarkdownIsExportTarget\": true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void M12b_DoesNotImplementMarkdownEditor()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("MarkdownEditor", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M12b_DoesNotImplementRoslynExecution()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.CodeAnalysis", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataReference", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M12b_DoesNotImplementVisionary()
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();

        Assert.DoesNotContain(model.Sections, section => string.Equals(section.Id, "visionary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExportPresenter_MarkdownCards_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-markdown-cards.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionMarkdownBodyManifestJsonPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_MarkdownInspector_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-markdown-inspector-roadmap.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "execution-roadmap",
                    SelectedCardId: "markdown-first-roadmap"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionMarkdownBodyManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M12bManifest_RecordsMarkdownAsCardBodyOnly()
    {
        string manifestPath = ExportMarkdownManifest();
        string json = File.ReadAllText(manifestPath);

        Assert.Contains("\"markdownAsCardBodyOnly\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"canonicalDocumentModel\": \"stack-of-typed-cards\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void M12bManifest_RecordsSingleFileMarkdownAsExportTarget()
    {
        string manifestPath = ExportMarkdownManifest();
        string json = File.ReadAllText(manifestPath);

        Assert.Contains("\"singleFileMarkdownIsExportTarget\": true", json, StringComparison.Ordinal);
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

        return result.OblivionMarkdownBodyManifestJsonPath!;
    }

    private static string CreateWorkspaceDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "machina-oblivion-markdown-m12b-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "pages"));
        Directory.CreateDirectory(Path.Combine(directory, "cards"));
        Directory.CreateDirectory(Path.Combine(directory, "body"));
        return directory;
    }

    private static void WriteWorkspaceWithMarkdownBody(string directory, string bodyPath, string? markdownFileContent)
    {
        File.WriteAllText(
            Path.Combine(directory, "workspace.oblivion.json"),
            """
            {
              "format": 1,
              "kind": "oblivion-workspace",
              "workspaceId": "markdown-test",
              "title": "Markdown Test Workspace",
              "defaultPageId": "execution-roadmap",
              "sections": [
                {
                  "id": "oblivion",
                  "title": "Oblivion",
                  "pages": [
                    {
                      "id": "execution-roadmap",
                      "title": "Execution Roadmap",
                      "asset": "pages/execution-roadmap.page.toml",
                      "cards": [ "cards/markdown.card.toml" ]
                    }
                  ]
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(directory, "pages", "execution-roadmap.page.toml"),
            """
            format = 1
            kind = "page"
            id = "execution-roadmap"
            title = "Execution Roadmap"
            """);

        File.WriteAllText(
            Path.Combine(directory, "cards", "markdown.card.toml"),
            $"""
            format = 1
            kind = "card"
            id = "markdown-card"
            card_kind = "note"
            status = "idle"
            title = "Markdown Card"

            [body]
            format = "copeland-markdown"
            path = "{bodyPath}"
            """);

        if (markdownFileContent is not null)
        {
            string bodyFilePath = Path.Combine(directory, bodyPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(bodyFilePath)!);
            File.WriteAllText(bodyFilePath, markdownFileContent);
        }
    }

    private static string GetSampleWorkspacePath()
    {
        return Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "workspace.oblivion.json");
    }

    private static string GetSampleCardPath(string fileName)
    {
        return Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "cards", fileName);
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
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-m12b-tests", Guid.NewGuid().ToString("N"));
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
