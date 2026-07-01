using Machina.Dominatus.Rendering.Commands;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionDocsDogfoodM12dTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();

    [Fact]
    public void DocsDogfood_UsesCuratedDocsList()
    {
        Assert.Equal(
            [
                "docs/machina-oblivion-phase-closeout-m11g.md",
                "docs/machina-oblivion-workspace-persistence-m11d.md",
                "docs/machina-presenter-card-hardening-m11e.md",
                "docs/machina-test-suite-topology-m11b.md",
                "docs/machina-presenter-scrollbar-state-machine-m11c.md",
                "docs/copeland-markdown-frontend-m12a.md",
                "docs/machina-oblivion-markdown-body-integration-m12b.md",
                "docs/machina-oblivion-markdown-rendering-m12c.md",
                "docs/Aurelian/aurelian-monorepo-import-audit-m13a.md",
                "docs/Aurelian/aurelian-build-topology-m13b.md",
                "docs/Aurelian/architecture/aurelian-charter.md",
                "docs/Aurelian/architecture/dependency-policy.md",
                "docs/Aurelian/architecture/compositor-policy-mechanism-split.md",
                "docs/Aurelian/architecture/graphics-memory-allocation.md",
                "docs/Aurelian/architecture/mvp-roadmap.md",
                "docs/Aurelian/architecture/world-model-doctrine.md",
            ],
            OblivionDocsDogfoodCatalog.GetCuratedDocs());
    }

    [Fact]
    public void DocsDogfood_GeneratesStableCardIds()
    {
        IReadOnlyList<OblivionCard> docCards = GetDocCards();

        Assert.Equal(
            [
                "doc-machina-oblivion-phase-closeout-m11g",
                "doc-machina-oblivion-workspace-persistence-m11d",
                "doc-machina-presenter-card-hardening-m11e",
                "doc-machina-test-suite-topology-m11b",
                "doc-machina-presenter-scrollbar-state-machine-m11c",
                "doc-copeland-markdown-frontend-m12a",
                "doc-machina-oblivion-markdown-body-integration-m12b",
                "doc-machina-oblivion-markdown-rendering-m12c",
                "doc-aurelian-monorepo-import-audit-m13a",
                "doc-aurelian-build-topology-m13b",
                "doc-aurelian-charter",
                "doc-dependency-policy",
                "doc-compositor-policy-mechanism-split",
                "doc-graphics-memory-allocation",
                "doc-mvp-roadmap",
                "doc-world-model-doctrine",
            ],
            docCards.Select(card => card.Id.Value).ToArray());
    }

    [Fact]
    public void DocsDogfood_GeneratesIndexCard()
    {
        OblivionCard indexCard = GetDocsPage().Cards[0];

        Assert.Equal(OblivionDocsDogfoodCatalog.IndexCardId, indexCard.Id.Value);
        Assert.Equal(OblivionCardKind.Status, indexCard.Kind);
        Assert.Contains("Docs loaded:", indexCard.Body.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_IndexCardSummarizesLoadedDocsAndDiagnostics()
    {
        OblivionCard indexCard = GetDocsPage().Cards[0];
        IReadOnlyList<OblivionCard> docCards = GetDocCards();

        Assert.Contains($"Docs loaded: {docCards.Count}", indexCard.Body.RawText, StringComparison.Ordinal);
        Assert.Contains("Aurelian docs loaded: 8", indexCard.Body.RawText, StringComparison.Ordinal);
        Assert.Contains($"Cards generated: {docCards.Count + 1}", indexCard.Body.RawText, StringComparison.Ordinal);
        Assert.Contains($"Diagnostics total: {docCards.Sum(card => card.Body.Diagnostics.Count)}", indexCard.Body.RawText, StringComparison.Ordinal);
        Assert.Contains(
            $"Aurelian diagnostics: {docCards.Where(IsAurelianDoc).Sum(card => card.Body.Diagnostics.Count)}",
            indexCard.Body.RawText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_LoadsSelectedDocsFromRepo()
    {
        IReadOnlyList<OblivionCard> docCards = GetDocCards();

        Assert.Equal(
            OblivionDocsDogfoodCatalog.GetCuratedDocs(),
            docCards.Select(card => Assert.IsType<string>(card.SourcePath)).ToArray());
    }

    [Fact]
    public void DocsDogfood_CompilesDocsThroughCopelandMarkdown()
    {
        Assert.All(GetDocCards(), card => Assert.NotNull(card.Body.DocumentMir));
    }

    [Fact]
    public void DocsDogfood_PreservesSourcePaths()
    {
        OblivionCard card = GetDocCards()[0];

        Assert.Equal("docs/machina-oblivion-phase-closeout-m11g.md", card.SourcePath);
        Assert.Equal(card.SourcePath, card.Body.BodySourcePath);
    }

    [Fact]
    public void DocsDogfood_IncludesAurelianAuditDocs()
    {
        IReadOnlyList<OblivionCard> cards = GetDocCards();

        Assert.Contains(cards, card => card.Id.Value == "doc-aurelian-monorepo-import-audit-m13a");
        Assert.Contains(cards, card => card.Id.Value == "doc-aurelian-build-topology-m13b");
    }

    [Fact]
    public void DocsDogfood_IncludesAurelianArchitectureDocs()
    {
        IReadOnlyList<OblivionCard> cards = GetDocCards();

        Assert.Contains(cards, card => card.SourcePath == "docs/Aurelian/architecture/aurelian-charter.md");
        Assert.Contains(cards, card => card.SourcePath == "docs/Aurelian/architecture/dependency-policy.md");
        Assert.Contains(cards, card => card.SourcePath == "docs/Aurelian/architecture/compositor-policy-mechanism-split.md");
        Assert.Contains(cards, card => card.SourcePath == "docs/Aurelian/architecture/graphics-memory-allocation.md");
        Assert.Contains(cards, card => card.SourcePath == "docs/Aurelian/architecture/mvp-roadmap.md");
        Assert.Contains(cards, card => card.SourcePath == "docs/Aurelian/architecture/world-model-doctrine.md");
    }

    [Fact]
    public void DocsDogfood_AurelianDocsUseStableCardIds()
    {
        IReadOnlyList<OblivionCard> cards = GetDocCards().Where(IsAurelianDoc).ToArray();

        Assert.All(cards, card => Assert.StartsWith("doc-", card.Id.Value, StringComparison.Ordinal));
        Assert.Contains(cards, card => card.Id.Value == "doc-aurelian-monorepo-import-audit-m13a");
        Assert.Contains(cards, card => card.Id.Value == "doc-aurelian-build-topology-m13b");
        Assert.Contains(cards, card => card.Id.Value == "doc-aurelian-charter");
    }

    [Fact]
    public void DocsDogfood_AurelianDocsPreserveSourcePaths()
    {
        OblivionCard card = Assert.Single(GetDocCards(), card => card.Id.Value == "doc-aurelian-monorepo-import-audit-m13a");

        Assert.Equal("docs/Aurelian/aurelian-monorepo-import-audit-m13a.md", card.SourcePath);
        Assert.Equal(card.SourcePath, card.Body.BodySourcePath);
    }

    [Fact]
    public void DocsDogfood_AurelianDocsCompileThroughCopelandMarkdown()
    {
        Assert.All(GetDocCards().Where(IsAurelianDoc), card => Assert.NotNull(card.Body.DocumentMir));
    }

    [Fact]
    public void DocsDogfood_DoesNotCrashOnUnsupportedSyntax()
    {
        PresenterPageRenderResult page = RenderDocsPage(GetDocCards()[0].Id.Value);

        Assert.NotEmpty(page.Frame.RenderCommands);
    }

    [Fact]
    public void DocsDogfood_DiagnosticsArePerDoc()
    {
        IReadOnlyList<OblivionCard> docCards = GetDocCards();

        Assert.All(docCards, card => Assert.NotNull(card.Body.Diagnostics));
        Assert.All(docCards, card => Assert.Equal(card.SourcePath, card.Body.BodySourcePath));
    }

    [Fact]
    public void DocsDogfood_AurelianDiagnosticsArePerDoc()
    {
        IReadOnlyList<OblivionCard> cards = GetDocCards().Where(IsAurelianDoc).ToArray();

        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.Equal(card.SourcePath, card.Body.BodySourcePath));
        Assert.All(cards, card => Assert.Contains("aurelian", card.Tags, StringComparer.Ordinal));
    }

    [Fact]
    public void DocsDogfood_IndexSummarizesAurelianDocs()
    {
        string? rawText = GetDocsPage().Cards[0].Body.RawText;
        Assert.NotNull(rawText);
        string text = rawText;

        Assert.Contains("Aurelian docs loaded: 8", text, StringComparison.Ordinal);
        Assert.Contains("Aurelian diagnostics:", text, StringComparison.Ordinal);
        Assert.Contains("Aurelian docs are dogfood inputs, not runtime or presenter integration behavior.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_CompactCardsShowFirstHeadingOrFileName()
    {
        PresenterPageRenderResult page = RenderDocsPage(GetDocCards()[0].Id.Value);
        string text = PageText(page);

        Assert.Contains("Machina Oblivion Phase Closeout M11g", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_InspectorShowsRenderedDoc()
    {
        OblivionCard card = GetDocCards()[0];
        string text = string.Join(Environment.NewLine, OblivionMarkdownBody.BuildInspectorLines(card.Body));

        Assert.Contains("M11g closes out the M11 Oblivion substrate phase.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_InspectorShowsSourcePath()
    {
        OblivionCard card = GetDocCards()[0];

        Assert.Equal("docs/machina-oblivion-phase-closeout-m11g.md", card.SourcePath);
        Assert.Equal("docs/machina-oblivion-phase-closeout-m11g.md", card.Body.BodySourcePath);
    }

    [Fact]
    public void DocsDogfood_InspectorShowsDiagnosticsPanelWhenNeeded()
    {
        OblivionCard card = GetDocCards().First();
        string text = PageText(RenderDocsPage(card.Id.Value));

        Assert.Contains("Markdown diagnostics", text, StringComparison.Ordinal);

        if (card.Body.Diagnostics.Count == 0)
        {
            Assert.Contains("No Markdown diagnostics.", text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("COPE-MD-", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DocsDogfood_OblivionPageRemainsStackOfCards()
    {
        OblivionWorkspacePage docsPage = GetDocsPage();

        Assert.True(docsPage.Cards.Count > 1);
        Assert.Equal(OblivionDocsDogfoodCatalog.IndexCardId, docsPage.Cards[0].Id.Value);
    }

    [Fact]
    public void DocsDogfood_DoesNotTreatMarkdownFileAsWholePage()
    {
        OblivionWorkspacePage docsPage = GetDocsPage();

        Assert.Equal("docs", docsPage.Id);
        Assert.Equal(OblivionDocsDogfoodCatalog.GetCuratedDocs().Count + 1, docsPage.Cards.Count);
    }

    [Fact]
    public void DocsDogfood_DoesNotImplementEditor()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("MarkdownEditor", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_DoesNotImplementFileWatcher()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("FileSystemWatcher", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_DoesNotImplementRoslynExecution()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.CodeAnalysis", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsDogfood_DoesNotImplementVisionary()
    {
        Assert.DoesNotContain(Model.Sections, section => string.Equals(section.Id, "visionary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DocsDogfood_DoesNotImplementSingleFileMarkdownExport()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WriteDocsDogfoodManifest(outputDirectory);
            string json = File.ReadAllText(jsonPath);

            Assert.Contains("\"singleFileMarkdownExportImplemented\": false", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_DocsDogfoodIndex_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-docs-dogfood-index.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    SelectedCardId: OblivionDocsDogfoodCatalog.IndexCardId),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionDocsDogfoodManifestJsonPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_DocsDogfoodDoc_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-docs-dogfood-closeout-doc.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    SelectedCardId: "doc-machina-oblivion-phase-closeout-m11g"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionDocsDogfoodManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_AurelianDocsIndex_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-aurelian-docs-dogfood-index.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    SelectedCardId: OblivionDocsDogfoodCatalog.IndexCardId),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_AurelianBuildTopologyDoc_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-aurelian-docs-build-topology.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    SelectedCardId: "doc-aurelian-build-topology-m13b"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void DocsDogfoodManifest_WritesJsonAndText()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WriteDocsDogfoodManifest(outputDirectory);

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(textPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void DocsDogfoodManifest_RecordsDocsAndDiagnostics()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WriteDocsDogfoodManifest(outputDirectory);
            string json = File.ReadAllText(jsonPath);

            Assert.Contains("\"milestone\": \"M12d\"", json, StringComparison.Ordinal);
            Assert.Contains("\"kind\": \"oblivion-docs-dogfood\"", json, StringComparison.Ordinal);
            Assert.Contains("\"sourcePath\": \"docs/machina-oblivion-phase-closeout-m11g.md\"", json, StringComparison.Ordinal);
            Assert.Contains("\"diagnosticsTotal\":", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M13cManifest_RecordsAurelianTestAndDocsStatus()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            AurelianTestDocsDogfoodManifestData data = new(
                AurelianTestNormalizationFixed: true,
                AurelianSolutionRestoreStatus: "passed",
                AurelianSolutionBuildStatus: "passed",
                AurelianSolutionTestStatus: "passed",
                ShaderLineEndingNormalization: true,
                AurelianDocsLoaded: 8,
                AurelianDocsDiagnostics: 3,
                DocsLoadedTotal: OblivionDocsDogfoodCatalog.GetCuratedDocs().Count,
                DiagnosticsTotal: 7,
                DeferredWork:
                [
                    "SDSL-V migration into Copeland",
                    "Machina.Aurelian bridge",
                    "Vulkan presenter integration",
                ]);

            (string jsonPath, string textPath) = AurelianTestDocsDogfoodManifest.Write(outputDirectory, data);
            string json = File.ReadAllText(jsonPath);
            string text = File.ReadAllText(textPath);

            Assert.Contains("\"milestone\": \"M13c\"", json, StringComparison.Ordinal);
            Assert.Contains("\"kind\": \"aurelian-test-normalization-docs-dogfood\"", json, StringComparison.Ordinal);
            Assert.Contains("\"aurelianTestNormalizationFixed\": true", json, StringComparison.Ordinal);
            Assert.Contains("\"shaderLineEndingNormalization\": true", json, StringComparison.Ordinal);
            Assert.Contains("\"sdslvMigrationPerformed\": false", json, StringComparison.Ordinal);
            Assert.Contains("aurelianSolutionTestStatus=passed", text, StringComparison.Ordinal);
            Assert.Contains("repoRenamed=false", text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M13c_DoesNotMoveSdslvToCopeland()
    {
        string combinedText = string.Join(
            Environment.NewLine,
            GetSourceFiles("src", "Copeland.Markdown")
                .Concat(GetSourceFiles("src", "Copeland.Cli"))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("namespace Copeland.Shaders", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M13c_DoesNotImplementMachinaAurelianBridge()
    {
        string combinedText = string.Join(
            Environment.NewLine,
            GetSourceFiles("src", "Machina.Core")
                .Concat(GetSourceFiles("src", "Machina.Standard"))
                .Concat(GetSourceFiles("src", "Machina.Layout"))
                .Concat(GetSourceFiles("src", "Machina.Runtime"))
                .Concat(GetSourceFiles("src", "Machina.Pipeline"))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("Aurelian.Runtime", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Graphics", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M13c_DoesNotImplementVulkanPresenterIntegration()
    {
        string combinedText = string.Join(
            Environment.NewLine,
            GetSourceFiles("src", "Machina.Core")
                .Concat(GetSourceFiles("src", "Machina.Standard"))
                .Concat(GetSourceFiles("src", "Machina.Layout"))
                .Concat(GetSourceFiles("src", "Machina.Runtime"))
                .Concat(GetSourceFiles("src", "Machina.Pipeline"))
                .Select(File.ReadAllText));

        Assert.DoesNotContain("Silk.NET.Vulkan", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Vulkan", combinedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M13c_DoesNotRenameRepo()
    {
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "Copeland.slnx")));
        Assert.True(File.Exists(Path.Combine(GetRepositoryRoot(), "Copeland.Slow.slnx")));
    }

    private static OblivionWorkspacePage GetDocsPage()
    {
        OblivionWorkspaceLoadResult loadResult = OblivionWorkspaceLoader.Load(GetSampleWorkspacePath(), useCache: false);

        return Assert.Single(
            loadResult.Workspace!.Sections.Single().Pages,
            page => page.PresenterPageId == OblivionWorkbenchCatalog.DocsPageId);
    }

    private static IReadOnlyList<OblivionCard> GetDocCards()
    {
        return GetDocsPage().Cards
            .Where(card => !string.Equals(card.Id.Value, OblivionDocsDogfoodCatalog.IndexCardId, StringComparison.Ordinal))
            .ToArray();
    }

    private static PresenterPageRenderResult RenderDocsPage(string selectedCardId)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "docs")
            .WithSelectedCard(OblivionWorkbenchCatalog.DocsPageId, selectedCardId);

        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.DocsPageId,
            DemoState.Default,
            StandardTheme.Default,
            new PresenterProofOptions(),
            PresenterNavigationLayout.Default.ContentVisibleWidth,
            state);
    }

    private static bool IsAurelianDoc(OblivionCard card)
    {
        return card.SourcePath?.StartsWith("docs/Aurelian/", StringComparison.Ordinal) == true;
    }

    private static string PageText(PresenterPageRenderResult page)
    {
        return string.Join(
            Environment.NewLine,
            page.Frame.RenderCommands
                .OfType<DrawTextCommand>()
                .Select(command => command.Text));
    }

    private static IEnumerable<string> GetSourceFiles(params string[] segments)
    {
        string[] pathParts = new string[segments.Length + 1];
        pathParts[0] = GetRepositoryRoot();
        Array.Copy(segments, 0, pathParts, 1, segments.Length);
        return Directory.GetFiles(Path.Combine(pathParts), "*.cs", SearchOption.AllDirectories);
    }

    private static string GetSampleWorkspacePath()
    {
        return Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "workspace.oblivion.json");
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string CreateOutputDirectory()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-m12d-tests", Guid.NewGuid().ToString("N"));
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
