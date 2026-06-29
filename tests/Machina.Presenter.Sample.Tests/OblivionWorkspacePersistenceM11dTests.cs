using Machina.Core.Flat;
using Machina.Dominatus.Rendering.Commands;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionWorkspacePersistenceM11dTests
{
    [Fact]
    public void OblivionWorkspaceJson_LoadsRootManifest()
    {
        string json = File.ReadAllText(GetSampleWorkspacePath());

        OblivionWorkspaceJsonReadResult result = OblivionWorkspaceJsonReader.Read(json, GetSampleWorkspacePath());

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("machina-sample", result.Manifest!.WorkspaceId);
        Assert.Equal("Machina Sample Workspace", result.Manifest.Title);
        Assert.Equal("cards", result.Manifest.DefaultPageId);
    }

    [Fact]
    public void OblivionWorkspaceJson_PreservesSectionPageOrder()
    {
        const string json = """
            {
              "format": 1,
              "kind": "oblivion-workspace",
              "workspaceId": "ordered",
              "title": "Ordered Workspace",
              "defaultPageId": "first",
              "sections": [
                {
                  "id": "oblivion",
                  "title": "Oblivion",
                  "pages": [
                    { "id": "first", "title": "First", "cards": [] },
                    { "id": "second", "title": "Second", "cards": [] }
                  ]
                },
                {
                  "id": "notes",
                  "title": "Notes",
                  "pages": [
                    { "id": "third", "title": "Third", "cards": [] }
                  ]
                }
              ]
            }
            """;

        OblivionWorkspaceJsonReadResult result = OblivionWorkspaceJsonReader.Read(json);

        Assert.NotNull(result.Manifest);
        Assert.Equal(["oblivion", "notes"], result.Manifest!.Sections.Select(section => section.Id).ToArray());
        Assert.Equal(["first", "second"], result.Manifest.Sections[0].Pages.Select(page => page.Id).ToArray());
    }

    [Fact]
    public void OblivionWorkspaceJson_RejectsUnsupportedFormat()
    {
        string json = File.ReadAllText(GetSampleWorkspacePath()).Replace("\"format\": 1", "\"format\": 99", StringComparison.Ordinal);

        OblivionWorkspaceJsonReadResult result = OblivionWorkspaceJsonReader.Read(json);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "unsupported-format");
    }

    [Fact]
    public void OblivionWorkspaceJson_RejectsUnknownKind()
    {
        string json = File.ReadAllText(GetSampleWorkspacePath()).Replace("\"oblivion-workspace\"", "\"mystery-workspace\"", StringComparison.Ordinal);

        OblivionWorkspaceJsonReadResult result = OblivionWorkspaceJsonReader.Read(json);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "unknown-workspace-kind");
    }

    [Fact]
    public void OblivionWorkspaceJson_RejectsDuplicateSectionIds()
    {
        const string json = """
            {
              "format": 1,
              "kind": "oblivion-workspace",
              "workspaceId": "dup-sections",
              "title": "Duplicate Sections",
              "defaultPageId": "cards",
              "sections": [
                {
                  "id": "oblivion",
                  "title": "One",
                  "pages": [
                    { "id": "cards", "title": "Cards", "cards": [] }
                  ]
                },
                {
                  "id": "oblivion",
                  "title": "Two",
                  "pages": [
                    { "id": "artifacts", "title": "Artifacts", "cards": [] }
                  ]
                }
              ]
            }
            """;

        OblivionWorkspaceJsonReadResult result = OblivionWorkspaceJsonReader.Read(json);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "duplicate-section-id");
    }

    [Fact]
    public void OblivionWorkspaceJson_RejectsDuplicatePageIds()
    {
        const string json = """
            {
              "format": 1,
              "kind": "oblivion-workspace",
              "workspaceId": "dup-pages",
              "title": "Duplicate Pages",
              "defaultPageId": "cards",
              "sections": [
                {
                  "id": "oblivion",
                  "title": "One",
                  "pages": [
                    { "id": "cards", "title": "Cards", "cards": [] },
                    { "id": "cards", "title": "Cards Again", "cards": [] }
                  ]
                }
              ]
            }
            """;

        OblivionWorkspaceJsonReadResult result = OblivionWorkspaceJsonReader.Read(json);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "duplicate-page-id");
    }

    [Fact]
    public void OblivionCardToml_LoadsRequiredFields()
    {
        string toml = File.ReadAllText(GetSampleCardPath("intro.card.toml"));

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml, GetSampleCardPath("intro.card.toml"));

        Assert.NotNull(result.Document);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("oblivion-intro-note-card", result.Document!.Id);
        Assert.Equal("note", result.Document.CardKind);
        Assert.Equal("idle", result.Document.Status);
        Assert.Equal("Oblivion workbench substrate", result.Document.Title);
    }

    [Fact]
    public void OblivionCardToml_LoadsBodyText()
    {
        string toml = File.ReadAllText(GetSampleCardPath("intro.card.toml"));

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml);

        Assert.Contains("Workspace structure now lives in a JSON root manifest.", result.Document!.Body.Text);
    }

    [Fact]
    public void OblivionCardToml_LoadsActionsAsMetadataOnly()
    {
        string toml = File.ReadAllText(GetSampleCardPath("ui-preview-placeholder.card.toml"));

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml);

        OblivionCardActionDocument action = Assert.Single(result.Document!.Actions);
        Assert.Equal("open-preview", action.Id);
        Assert.Equal("Open preview", action.Label);
        Assert.False(action.Enabled);
    }

    [Fact]
    public void OblivionCardToml_LoadsArtifactsAsMetadataOnly()
    {
        string toml = File.ReadAllText(GetSampleCardPath("artifact-placeholder.card.toml"));

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml);

        Assert.Equal(2, result.Document!.Artifacts.Count);
        Assert.All(result.Document.Artifacts, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.Asset)));
    }

    [Fact]
    public void OblivionCardToml_RejectsUnknownCardKind()
    {
        string toml = File.ReadAllText(GetSampleCardPath("intro.card.toml")).Replace("card_kind = \"note\"", "card_kind = \"mystery\"", StringComparison.Ordinal);

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "unknown-card-kind");
    }

    [Fact]
    public void OblivionCardToml_RejectsUnknownStatus()
    {
        string toml = File.ReadAllText(GetSampleCardPath("intro.card.toml")).Replace("status = \"idle\"", "status = \"mystery\"", StringComparison.Ordinal);

        OblivionCardTomlReadResult result = OblivionCardTomlReader.Read(toml);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "unknown-card-status");
    }

    [Fact]
    public void OblivionCardToml_RoundTripsDeterministically()
    {
        string toml = File.ReadAllText(GetSampleCardPath("artifact-placeholder.card.toml"));
        OblivionCardAssetDocument document = Assert.IsType<OblivionCardAssetDocument>(OblivionCardTomlReader.Read(toml).Document);

        string firstWrite = OblivionCardTomlWriter.Write(document);
        OblivionCardAssetDocument reparsed = Assert.IsType<OblivionCardAssetDocument>(OblivionCardTomlReader.Read(firstWrite).Document);
        string secondWrite = OblivionCardTomlWriter.Write(reparsed);

        Assert.Equal(firstWrite, secondWrite);
    }

    [Fact]
    public void OblivionWorkspaceLoader_RejectsAbsoluteAssetPaths()
    {
        string directory = CreateWorkspaceDirectory();

        try
        {
            WriteWorkspace(
                directory,
                pageAsset: Path.Combine(directory, "pages", "cards.page.toml"),
                cardAsset: "cards/intro.card.toml");

            OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(Path.Combine(directory, "workspace.oblivion.json"), useCache: false);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "absolute-path-not-allowed");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void OblivionWorkspaceLoader_RejectsPathTraversal()
    {
        string directory = CreateWorkspaceDirectory();

        try
        {
            WriteWorkspace(
                directory,
                pageAsset: "../outside.page.toml",
                cardAsset: "cards/intro.card.toml");

            OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(Path.Combine(directory, "workspace.oblivion.json"), useCache: false);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "path-traversal-not-allowed");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void OblivionWorkspaceLoader_ReportsMissingCardAsset()
    {
        string directory = CreateWorkspaceDirectory();

        try
        {
            WriteWorkspace(
                directory,
                pageAsset: "pages/cards.page.toml",
                cardAsset: "cards/missing.card.toml");

            OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(Path.Combine(directory, "workspace.oblivion.json"), useCache: false);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "missing-card-asset");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void OblivionWorkspaceLoader_ReportsMissingPageAsset()
    {
        string directory = CreateWorkspaceDirectory();

        try
        {
            WriteWorkspace(
                directory,
                pageAsset: "pages/missing.page.toml",
                cardAsset: "cards/intro.card.toml");

            OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(Path.Combine(directory, "workspace.oblivion.json"), useCache: false);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "missing-page-asset");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void OblivionWorkspaceLoader_LoadsSampleWorkspace()
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(GetSampleWorkspacePath(), useCache: false);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Workspace);
        Assert.Equal("machina-sample", result.Workspace!.WorkspaceId);
    }

    [Fact]
    public void OblivionWorkspaceLoader_LoadsCardsIntoPages()
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(GetSampleWorkspacePath(), useCache: false);

        OblivionWorkspacePage cardsPage = Assert.Single(
            result.Workspace!.Sections.Single().Pages,
            page => page.PresenterPageId == "oblivion.cards");

        Assert.Equal(7, cardsPage.Cards.Count);
        Assert.Equal("oblivion-intro-note-card", cardsPage.Cards[0].Id.Value);
    }

    [Fact]
    public void OblivionWorkspaceLoader_ProducesStableDiagnostics()
    {
        string directory = CreateWorkspaceDirectory();

        try
        {
            WriteWorkspace(
                directory,
                pageAsset: "../outside.page.toml",
                cardAsset: "cards/missing.card.toml");

            string manifestPath = Path.Combine(directory, "workspace.oblivion.json");
            OblivionWorkspaceLoadResult first = OblivionWorkspaceLoader.Load(manifestPath, useCache: false);
            OblivionWorkspaceLoadResult second = OblivionWorkspaceLoader.Load(manifestPath, useCache: false);

            Assert.Equal(
                first.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray(),
                second.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void OblivionWorkspaceLoader_DoesNotExecuteCodeCards()
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(GetSampleWorkspacePath(), useCache: false);

        OblivionCard factCard = Assert.Single(
            result.Workspace!.Sections.Single().Pages.Single(page => page.Id == "cards").Cards,
            card => card.Kind == OblivionCardKind.CodeFact);
        OblivionCard theoryCard = Assert.Single(
            result.Workspace.Sections.Single().Pages.Single(page => page.Id == "cards").Cards,
            card => card.Kind == OblivionCardKind.CodeTheory);

        Assert.Equal(OblivionCardStatus.Deferred, factCard.Status);
        Assert.Equal(OblivionCardStatus.Deferred, theoryCard.Status);
        Assert.All(factCard.Actions, action => Assert.False(action.Enabled));
        Assert.All(theoryCard.Actions, action => Assert.False(action.Enabled));
    }

    [Fact]
    public void PresenterShell_OblivionCards_LoadFromWorkspace()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId);
        List<string> text = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Oblivion workbench substrate", text);
        Assert.Contains("JSON", string.Join(" ", text));
        Assert.Contains("[Fact]", text);
    }

    [Fact]
    public void PresenterShell_OblivionExecutionRoadmap_LoadsFromWorkspace()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.ExecutionRoadmapPageId);
        List<string> text = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Markdown-first roadmap", text);
        Assert.Contains("Visionary future card", text);
    }

    [Fact]
    public void PresenterShell_OblivionArtifacts_LoadsFromWorkspace()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.ArtifactsPageId);
        List<string> text = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Artifact lane placeholder", text);
        Assert.Contains("Artifact policy", text);
    }

    [Fact]
    public void PresenterShell_OblivionWorkspaceLoadFailure_ShowsErrorCard()
    {
        PresenterPageRenderResult page = RenderPage(
            OblivionWorkbenchCatalog.CardsPageId,
            new PresenterProofOptions(OblivionWorkspacePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing", "workspace.oblivion.json")));
        List<string> text = page.Frame.RenderCommands.OfType<DrawTextCommand>().Select(command => command.Text).ToList();

        Assert.Contains("Oblivion workspace load failed", text);
    }

    [Fact]
    public void ExportPresenter_OblivionWorkspaceCards_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-workspace-cards.png"),
                new PresenterProofOptions(),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards"),
                StandardTheme.Default);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("oblivion.cards", result.NavigationPageId);
            Assert.True(File.Exists(result.OblivionManifestJsonPath!));
            Assert.True(File.Exists(result.OblivionManifestTextPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M11d_DoesNotReferenceRoslynExecution()
    {
        string combinedText = string.Join(
            Environment.NewLine,
            GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.CodeAnalysis", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataReference", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyLoadContext", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M11d_DoesNotRunFactOrTheoryCards()
    {
        OblivionCard factCard = Assert.Single(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId),
            card => card.Id.Value == "oblivion-code-fact-card");

        Assert.Contains("Deferred until M13+.", factCard.BodyLines);
        Assert.DoesNotContain("Xunit.Sdk", string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText)), StringComparison.Ordinal);
    }

    [Fact]
    public void M11d_DoesNotImplementVisionaryEditor()
    {
        PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();

        Assert.DoesNotContain(model.Sections, section => section.Id == "visionary");
        Assert.DoesNotContain(
            model.Sections.SelectMany(section => section.Tabs),
            tab => tab.PageId.Contains("visionary", StringComparison.OrdinalIgnoreCase));
    }

    private static PresenterPageRenderResult RenderPage(string pageId, PresenterProofOptions? proofOptions = null)
    {
        return PresenterNavigationCatalog.RenderPage(
            pageId,
            DemoState.Default,
            StandardTheme.Default,
            proofOptions ?? new PresenterProofOptions(),
            PresenterNavigationLayout.Default.ContentVisibleWidth);
    }

    private static string GetSampleWorkspacePath()
    {
        return Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "workspace.oblivion.json");
    }

    private static string GetSampleCardPath(string fileName)
    {
        return Path.Combine(GetRepositoryRoot(), "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "cards", fileName);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static IEnumerable<string> GetSourceFiles(params string[] segments)
    {
        string[] pathParts = new string[segments.Length + 1];
        pathParts[0] = GetRepositoryRoot();
        Array.Copy(segments, 0, pathParts, 1, segments.Length);
        string directory = Path.Combine(pathParts);
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m11d-tests", Guid.NewGuid().ToString("N"));
    }

    private static string CreateWorkspaceDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "machina-oblivion-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "pages"));
        Directory.CreateDirectory(Path.Combine(directory, "cards"));
        return directory;
    }

    private static void WriteWorkspace(string directory, string pageAsset, string cardAsset)
    {
        File.WriteAllText(
            Path.Combine(directory, "workspace.oblivion.json"),
            OblivionWorkspaceJsonWriter.Write(
                new OblivionWorkspaceManifest(
                    OblivionWorkspaceValidator.SupportedFormat,
                    OblivionWorkspaceValidator.WorkspaceKind,
                    "test-workspace",
                    "Test Workspace",
                    "cards",
                    [
                        new OblivionWorkspaceSectionManifest(
                            "oblivion",
                            "Oblivion",
                            [
                                new OblivionWorkspacePageManifest(
                                    "cards",
                                    "Cards",
                                    pageAsset,
                                    [cardAsset])
                            ])
                    ])));

        File.WriteAllText(
            Path.Combine(directory, "pages", "cards.page.toml"),
            OblivionPageTomlWriter.Write(
                new OblivionPageAssetDocument(
                    OblivionWorkspaceValidator.SupportedFormat,
                    OblivionWorkspaceValidator.PageKind,
                    "cards",
                    "Cards",
                    "Test cards page",
                    ["test"])));

        File.WriteAllText(
            Path.Combine(directory, "cards", "intro.card.toml"),
            OblivionCardTomlWriter.Write(
                new OblivionCardAssetDocument(
                    OblivionWorkspaceValidator.SupportedFormat,
                    OblivionWorkspaceValidator.CardKind,
                    "intro",
                    "note",
                    "idle",
                    "Intro",
                    null,
                    ["test"],
                    new OblivionCardBodyDocument("plain", "Hello", null),
                    [],
                    [])));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
