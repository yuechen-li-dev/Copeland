using System.Reflection;
using System.Text.Json;
using Machina.Dominatus.Rendering.Commands;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionPhaseCloseoutM11gTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();
    private static readonly StandardTheme Theme = StandardTheme.Default;

    [Fact]
    public void OblivionPhaseCloseoutManifest_WritesJsonAndText()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WritePhaseCloseoutManifest(outputDirectory);

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(textPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void OblivionPhaseCloseoutManifest_RecordsMarkdownNext()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WritePhaseCloseoutManifest(outputDirectory);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.True(document.RootElement.GetProperty("markdownNext").GetBoolean());
            Assert.False(document.RootElement.GetProperty("markdownImplemented").GetBoolean());
            Assert.Equal("M12 Markdown document/card support", document.RootElement.GetProperty("nextPhase").GetString());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void OblivionPhaseCloseoutManifest_RecordsExecutionDeferred()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WritePhaseCloseoutManifest(outputDirectory);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.False(document.RootElement.GetProperty("executionEnabled").GetBoolean());
            Assert.False(document.RootElement.GetProperty("roslynEnabled").GetBoolean());
            Assert.False(document.RootElement.GetProperty("xunitEnabled").GetBoolean());
            Assert.Equal("M13+", document.RootElement.GetProperty("factExecutionDeferredUntil").GetString());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void OblivionPhaseCloseoutManifest_RecordsVisionaryFutureOnly()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, _) = OblivionWorkbenchCatalog.WritePhaseCloseoutManifest(outputDirectory);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.False(document.RootElement.GetProperty("visionaryImplemented").GetBoolean());
            Assert.Contains(
                document.RootElement.GetProperty("deferredWork").EnumerateArray().Select(item => item.GetString()),
                item => string.Equals(item, "Visionary code editor/source workspace", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void OblivionPhaseCloseoutManifest_IsDeterministic()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string firstJsonPath, string firstTextPath) = OblivionWorkbenchCatalog.WritePhaseCloseoutManifest(outputDirectory);
            string firstJson = File.ReadAllText(firstJsonPath);
            string firstText = File.ReadAllText(firstTextPath);

            (string secondJsonPath, string secondTextPath) = OblivionWorkbenchCatalog.WritePhaseCloseoutManifest(outputDirectory);
            string secondJson = File.ReadAllText(secondJsonPath);
            string secondText = File.ReadAllText(secondTextPath);

            Assert.Equal(firstJson, secondJson);
            Assert.Equal(firstText, secondText);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void OblivionWorkspace_ContainsSubstrateStatusCard()
    {
        Assert.Contains(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId),
            card => card.Id.Value == "oblivion-substrate-status");
    }

    [Fact]
    public void OblivionWorkspace_ContainsMarkdownFirstRoadmapCard()
    {
        Assert.Contains(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.ExecutionRoadmapPageId),
            card => card.Id.Value == "markdown-first-roadmap");
    }

    [Fact]
    public void OblivionWorkspace_ContainsExecutionDeferredCard()
    {
        Assert.Contains(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.ExecutionRoadmapPageId),
            card => card.Id.Value == "execution-deferred");
    }

    [Fact]
    public void OblivionWorkspace_ContainsVisionaryFutureCard()
    {
        Assert.Contains(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.ExecutionRoadmapPageId),
            card => card.Id.Value == "visionary-future");
    }

    [Fact]
    public void OblivionWorkspace_CodeFactCardsRemainDeferred()
    {
        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.CreateAllCards();
        IReadOnlyList<OblivionCard> codeCards = cards
            .Where(card => card.Kind is OblivionCardKind.CodeFact or OblivionCardKind.CodeTheory)
            .ToArray();

        Assert.NotEmpty(codeCards);
        Assert.All(codeCards, card =>
        {
            Assert.Equal(OblivionCardStatus.Deferred, card.Status);
            Assert.All(card.Actions, action => Assert.False(action.Enabled));
        });
    }

    [Fact]
    public void OblivionInspector_ShowsMarkdownRoadmapCard()
    {
        PresenterPageRenderResult page = RenderSelectedPage(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            "markdown-first-roadmap");
        string text = PageText(page);

        Assert.Contains("Markdown-first roadmap", text, StringComparison.Ordinal);
        Assert.Contains("Body format: Copeland Markdown", text, StringComparison.Ordinal);
        Assert.Contains("Kind: Note", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_ShowsExecutionDeferredNotice()
    {
        PresenterPageRenderResult page = RenderSelectedPage(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            "execution-deferred");
        string text = PageText(page);

        Assert.Contains("Execution deferred", text, StringComparison.Ordinal);
        Assert.Contains("Not executed in M11g.", text, StringComparison.Ordinal);
        Assert.Contains("Code Fact", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportPresenter_OblivionMarkdownRoadmap_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-markdown-roadmap.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "execution-roadmap",
                    SelectedCardId: "markdown-first-roadmap"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionPhaseCloseoutManifestJsonPath!));
            Assert.Contains(
                "oblivion.execution-roadmap:markdown-first-roadmap",
                File.ReadAllText(result.OblivionInspectorManifestTextPath!),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_OblivionExecutionDeferred_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-execution-deferred.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "execution-roadmap",
                    SelectedCardId: "execution-deferred"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionPhaseCloseoutManifestTextPath!));
            Assert.Contains(
                "oblivion.execution-roadmap:execution-deferred",
                File.ReadAllText(result.OblivionInspectorManifestTextPath!),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M11g_DoesNotReferenceRoslynExecution()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.CodeAnalysis", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataReference", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyLoadContext", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M11g_DoesNotRunFactOrTheoryCards()
    {
        PresenterPageRenderResult page = RenderSelectedPage(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            "execution-deferred");
        string text = PageText(page);
        OblivionCard selectedCard = Assert.Single(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.ExecutionRoadmapPageId),
            card => card.Id.Value == "execution-deferred");

        Assert.Contains("Not executed in M11g.", text, StringComparison.Ordinal);
        Assert.Contains("Execution result", text, StringComparison.Ordinal);
        Assert.Equal(OblivionCardStatus.Deferred, selectedCard.Status);
    }

    [Fact]
    public void M11g_DoesNotImplementMarkdownRenderer()
    {
        string combinedText = string.Join(
            Environment.NewLine,
            GetProjectFiles().Concat(GetSourceFiles("src")).Concat(GetSourceFiles("samples")).Select(File.ReadAllText));

        foreach (string forbidden in GetForbiddenMarkdownDependencyNames())
        {
            Assert.DoesNotContain(forbidden, combinedText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void M11g_DoesNotImplementMarkdownEditor()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("MarkdownEditor", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveMarkdown", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M11g_DoesNotImplementVisionaryEditor()
    {
        Assert.DoesNotContain(Model.Sections, section => section.Id == "visionary");
        Assert.DoesNotContain(GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText), source => source.Contains("VisionaryEditor", StringComparison.Ordinal));
    }

    private static PresenterPageRenderResult RenderSelectedPage(string pageId, string cardId)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", pageId == OblivionWorkbenchCatalog.CardsPageId ? "cards" : "execution-roadmap")
            .WithSelectedCard(pageId, cardId);
        return RenderPage(pageId, state);
    }

    private static PresenterPageRenderResult RenderPage(string pageId, PresenterNavigationState? state = null)
    {
        return PresenterNavigationCatalog.RenderPage(
            pageId,
            DemoState.Default,
            Theme,
            ProofOptions,
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

    private static IEnumerable<string> GetSourceFiles(params string[] segments)
    {
        string[] pathParts = new string[segments.Length + 1];
        pathParts[0] = GetRepositoryRoot();
        Array.Copy(segments, 0, pathParts, 1, segments.Length);
        string directory = Path.Combine(pathParts);
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path));
    }

    private static IEnumerable<string> GetProjectFiles()
    {
        string root = GetRepositoryRoot();
        string[] patterns = ["*.csproj", "*.props", "*.targets"];

        foreach (string pattern in patterns)
        {
            foreach (string path in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
            {
                if (!IsGeneratedPath(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> GetForbiddenMarkdownDependencyNames()
    {
        yield return string.Concat("Mark", "dig");
        yield return string.Concat("Common", "Mark");
        yield return string.Concat("Markdown", "Sharp");
        yield return string.Concat("Markdown", "Deep");
    }

    private static bool IsGeneratedPath(string path)
    {
        string normalized = path.Replace('/', '\\');
        return normalized.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m11g-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
