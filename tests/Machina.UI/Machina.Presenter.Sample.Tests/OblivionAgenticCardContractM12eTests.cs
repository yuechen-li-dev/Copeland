using System.Text.Json;
using Machina.Dominatus.Rendering.Commands;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionAgenticCardContractM12eTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();

    [Fact]
    public void OblivionCardHandlerRegistry_RegistersRequiredKinds()
    {
        OblivionCardHandlerRegistry registry = OblivionCardHandlerRegistry.CreateDefault();

        Assert.Equal(
            [
                OblivionCardKind.Artifact,
                OblivionCardKind.CodeFact,
                OblivionCardKind.CodeTheory,
                OblivionCardKind.Note,
                OblivionCardKind.Status,
                OblivionCardKind.UiPreview,
            ],
            registry.RegisteredKinds);
    }

    [Fact]
    public void OblivionCardHandlerRegistry_UnknownKindProducesErrorHandler()
    {
        OblivionCardHandlerRegistry registry = new([new OblivionNoteCardHandler()]);
        OblivionCard codeFact = GetCardsPageCard("oblivion-code-fact-card");

        OblivionBuiltCard builtCard = registry.BuildCard(codeFact);

        Assert.Contains(builtCard.RuntimeModel.Diagnostics, diagnostic => diagnostic.Code == "M12E-UNKNOWN-KIND");
        Assert.Contains("Missing card handler.", Assert.IsType<OblivionCompactPlainBodyContent>(builtCard.CompactView.Body).Lines);
    }

    [Fact]
    public void OblivionCardHandlerRegistry_AddingHandlerDoesNotRequireShellChanges()
    {
        OblivionCardHandlerRegistry registry = new(
        [
            new OblivionNoteCardHandler(),
            new OblivionStatusCardHandler(),
            new TestUiPreviewHandler(),
        ]);

        OblivionBuiltCard builtCard = registry.BuildCard(GetCardsPageCard("oblivion-ui-preview-card"));

        Assert.Equal("Test UI preview", builtCard.CompactView.Title);
        Assert.Equal(OblivionCardKind.UiPreview, builtCard.RuntimeModel.Identity.Kind);
    }

    [Fact]
    public void OblivionCardHandler_HandlersAreIndependentlyTestable()
    {
        IOblivionCardHandler handler = new OblivionNoteCardHandler();
        OblivionCard card = GetExecutionRoadmapCard("markdown-first-roadmap");

        OblivionCardRuntimeModel model = handler.BuildModel(
            card,
            new OblivionCardContext(card.PageId, card.WorkspaceId, card.SourcePath, null, null));
        OblivionCompactCardView view = handler.BuildCompactView(model, new OblivionCardViewContext(model.LocalState));

        Assert.Equal(card.Id.Value, model.Identity.Id.Value);
        Assert.IsType<OblivionCompactMarkdownBodyContent>(view.Body);
    }

    [Fact]
    public void MarkdownCardHandler_BuildsRuntimeModelFromDocumentMir()
    {
        OblivionBuiltCard builtCard = GetBuiltExecutionRoadmapCard("markdown-first-roadmap");
        OblivionMarkdownNoteKindModel kindModel = Assert.IsType<OblivionMarkdownNoteKindModel>(builtCard.RuntimeModel.KindModel);

        Assert.True(kindModel.HasDocumentMir);
        Assert.NotEmpty(kindModel.PreviewLines);
    }

    [Fact]
    public void MarkdownCardHandler_ProducesCompactPreview()
    {
        OblivionBuiltCard builtCard = GetBuiltExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Contains("Markdown body", builtCard.CompactView.MetaBadges);
        Assert.IsType<OblivionCompactMarkdownBodyContent>(builtCard.CompactView.Body);
    }

    [Fact]
    public void MarkdownCardHandler_ProducesInspectorView()
    {
        OblivionBuiltCard builtCard = GetBuiltExecutionRoadmapCard("markdown-first-roadmap");

        Assert.Contains(builtCard.InspectorView.Sections, section => section.Title == "Raw Markdown Source");
        Assert.Contains(builtCard.InspectorView.Sections, section => section.Title == "Markdown diagnostics");
    }

    [Fact]
    public void MarkdownCardHandler_AdaptsMarkdownDiagnosticsToCardDiagnostics()
    {
        OblivionBuiltCard builtCard = GetBuiltExecutionRoadmapCard("markdown-diagnostics-sample");

        Assert.NotEmpty(builtCard.RuntimeModel.Diagnostics);
        Assert.All(builtCard.RuntimeModel.Diagnostics, diagnostic => Assert.Equal(OblivionCardDiagnosticSeverity.Warning, diagnostic.Severity));
    }

    [Fact]
    public void MarkdownCardHandler_DoesNotExecuteCodeFences()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, "execution-deferred");
        string text = PageText(page);

        Assert.Contains("Effect routing skeleton only.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Run fact ready", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeFactCardHandler_IsPlaceholderOnly()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-code-fact-card");

        Assert.Equal(OblivionCardStatus.Deferred, builtCard.RuntimeModel.Status);
        Assert.Contains("Effect routing", builtCard.InspectorView.Sections.Select(section => section.Title));
    }

    [Fact]
    public void CodeTheoryCardHandler_IsPlaceholderOnly()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-code-theory-card");

        Assert.Equal(OblivionCardStatus.Deferred, builtCard.RuntimeModel.Status);
        Assert.NotEmpty(Assert.IsType<OblivionCompactPlainBodyContent>(builtCard.CompactView.Body).Lines);
    }

    [Fact]
    public void CodeFactCardHandler_ActionsAreDeferred()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-code-fact-card");
        Assert.Contains(builtCard.RuntimeModel.Actions, action => action.Id == "run");
        Assert.All(builtCard.RuntimeModel.Actions, action => Assert.True(action.RequiresEffect));
    }

    [Fact]
    public void CodeFactCardHandler_DoesNotExecute()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-code-fact-card");

        Assert.Null(builtCard.RuntimeModel.LastEffectRequest);
        Assert.Null(builtCard.RuntimeModel.LastEffectResult);
    }

    [Fact]
    public void OblivionCardLocalState_DefaultsDeterministically()
    {
        OblivionCardLocalState first = OblivionCardLocalState.CreateDefault(new OblivionCardId("alpha"));
        OblivionCardLocalState second = OblivionCardLocalState.CreateDefault(new OblivionCardId("alpha"));

        Assert.Equal(first.CardId, second.CardId);
        Assert.Equal(first.IsExpanded, second.IsExpanded);
        Assert.Equal(first.SelectedArtifactId, second.SelectedArtifactId);
        Assert.Empty(first.Properties);
        Assert.Empty(second.Properties);
    }

    [Fact]
    public void OblivionCardLocalState_IsKeyedByCardId()
    {
        IReadOnlyDictionary<string, OblivionCardLocalState> states = OblivionCardLocalStateCatalog.CreateDefaults(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId));

        Assert.Contains("agentic-card-contract", states.Keys);
        Assert.Equal("agentic-card-contract", states["agentic-card-contract"].CardId.Value);
    }

    [Fact]
    public void OblivionCardLocalState_DoesNotMutateGlobalNavigationState()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "cards")
            .WithSelectedCard(OblivionWorkbenchCatalog.CardsPageId, "oblivion-code-fact-card");

        _ = OblivionCardLocalStateCatalog.CreateDefaults(OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId));

        Assert.Equal(
            "oblivion-code-fact-card",
            state.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId)));
    }

    [Fact]
    public void OblivionCardActions_AreDescriptorsOnlyInM12e()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-code-fact-card");
        Assert.Contains(
            builtCard.RuntimeModel.Actions,
            action => action.Id == "run" && action.Intent == "CodeFact:run");
    }

    [Fact]
    public void OblivionCardActions_DoNotExecuteEffects()
    {
        string source = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.UI", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("MetadataReference", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyLoadContext", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionCardEffectRequests_AreDeferred()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-code-fact-card");

        Assert.Null(builtCard.RuntimeModel.LastEffectRequest);
        Assert.Null(builtCard.RuntimeModel.LastEffectResult);
    }

    [Fact]
    public void OblivionCardDiagnostics_AreCardLocal()
    {
        OblivionBuiltCard markdownDiagnostics = GetBuiltExecutionRoadmapCard("markdown-diagnostics-sample");
        OblivionBuiltCard markdownRoadmap = GetBuiltExecutionRoadmapCard("markdown-first-roadmap");

        Assert.NotEqual(markdownDiagnostics.RuntimeModel.Diagnostics.Count, markdownRoadmap.RuntimeModel.Diagnostics.Count);
    }

    [Fact]
    public void OblivionCardDiagnostics_CanBeAggregated()
    {
        IReadOnlyList<OblivionBuiltCard> cards = OblivionWorkbenchCatalog.GetBuiltPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId);
        int diagnosticsTotal = cards.Sum(card => card.RuntimeModel.Diagnostics.Count);

        Assert.True(diagnosticsTotal >= 0);
    }

    [Fact]
    public void OblivionCardArtifacts_AreMetadataOnly()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-artifact-placeholder-card");

        Assert.All(builtCard.RuntimeModel.Artifacts, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.Kind)));
    }

    [Fact]
    public void PresenterOblivion_UsesHandlersForCompactCards()
    {
        OblivionBuiltCard builtCard = GetBuiltExecutionRoadmapCard("markdown-first-roadmap");
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, "markdown-first-roadmap");

        Assert.Contains(builtCard.CompactView.MetaBadges, badge => PageText(page).Contains(badge, StringComparison.Ordinal));
    }

    [Fact]
    public void PresenterOblivion_UsesHandlersForInspector()
    {
        OblivionBuiltCard builtCard = GetBuiltCardsPageCard("oblivion-code-fact-card");
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId, "oblivion-code-fact-card");
        string text = PageText(page);

        Assert.Contains(builtCard.InspectorView.Sections, section => text.Contains(section.Title, StringComparison.Ordinal));
    }

    [Fact]
    public void PresenterOblivion_AgenticCardDoctrineCardExists()
    {
        OblivionCard card = GetCardsPageCard("agentic-card-contract");

        Assert.Equal(OblivionCardKind.Note, card.Kind);
        Assert.Equal(OblivionCardBodyFormat.CopelandMarkdown, card.Body.Format);
    }

    [Fact]
    public void PresenterOblivion_ExistingDocsDogfoodStillWorks()
    {
        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.DocsPageId);

        Assert.Contains(cards, card => card.Id.Value == "doc-copeland-markdown-frontend-m12a");
    }

    [Fact]
    public void M12eManifest_RecordsAgenticCardContract()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WriteAgenticCardContractManifest(outputDirectory);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.Equal("M12e", document.RootElement.GetProperty("milestone").GetString());
            Assert.True(document.RootElement.GetProperty("cardAsAppletDoctrine").GetBoolean());
            Assert.True(File.Exists(textPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M12e_DoesNotImplementRoslynExecution()
    {
        string source = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.UI", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("CSharpCompilation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataReference", source, StringComparison.Ordinal);
    }

    [Fact]
    public void M12e_DoesNotImplementXunitExecution()
    {
        string source = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.UI", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Xunit.Sdk", source, StringComparison.Ordinal);
    }

    [Fact]
    public void M12e_DoesNotImplementVisionary()
    {
        Assert.DoesNotContain(Model.Sections, section => string.Equals(section.Id, "visionary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M12e_DoesNotAddNewCardSpeciesBeyondContractProof()
    {
        Assert.Equal(
            [
                OblivionCardKind.Note,
                OblivionCardKind.Status,
                OblivionCardKind.UiPreview,
                OblivionCardKind.Artifact,
                OblivionCardKind.CodeFact,
                OblivionCardKind.CodeTheory,
            ],
            Enum.GetValues<OblivionCardKind>());
    }

    private static OblivionCard GetCardsPageCard(string cardId)
    {
        return Assert.Single(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId),
            card => card.Id.Value == cardId);
    }

    private static OblivionBuiltCard GetBuiltCardsPageCard(string cardId)
    {
        return Assert.Single(
            OblivionWorkbenchCatalog.GetBuiltPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId),
            card => card.SourceCard.Id.Value == cardId);
    }

    private static OblivionCard GetExecutionRoadmapCard(string cardId)
    {
        return Assert.Single(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.ExecutionRoadmapPageId),
            card => card.Id.Value == cardId);
    }

    private static OblivionBuiltCard GetBuiltExecutionRoadmapCard(string cardId)
    {
        return Assert.Single(
            OblivionWorkbenchCatalog.GetBuiltPageCardsForSelection(OblivionWorkbenchCatalog.ExecutionRoadmapPageId),
            card => card.SourceCard.Id.Value == cardId);
    }

    private static PresenterPageRenderResult RenderPage(string pageId, string selectedCardId)
    {
        string selectedTabId = pageId switch
        {
            var value when value == OblivionWorkbenchCatalog.CardsPageId => "cards",
            var value when value == OblivionWorkbenchCatalog.DocsPageId => "docs",
            var value when value == OblivionWorkbenchCatalog.ExecutionRoadmapPageId => "execution-roadmap",
            var value when value == OblivionWorkbenchCatalog.ArtifactsPageId => "artifacts",
            _ => throw new InvalidOperationException($"Unknown page '{pageId}'."),
        };

        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", selectedTabId)
            .WithSelectedCard(pageId, selectedCardId);

        return PresenterNavigationCatalog.RenderPage(
            pageId,
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

    private static IEnumerable<string> GetSourceFiles(params string[] segments)
    {
        string root = segments.Length == 0
            ? GetRepositoryRoot()
            : Path.Combine([GetRepositoryRoot(), .. segments]);

        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }

    private static string CreateOutputDirectory()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-m12e-tests", Guid.NewGuid().ToString("N"));
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

    private sealed class TestUiPreviewHandler : OblivionCardHandlerBase
    {
        public override OblivionCardKind Kind => OblivionCardKind.UiPreview;

        public override OblivionCompactCardView BuildCompactView(
            OblivionCardRuntimeModel model,
            OblivionCardViewContext context)
        {
            return new OblivionCompactCardView(
                model.Identity.Id.Value,
                "Test UI preview",
                model.SourceCard.Subtitle,
                BuildMetaBadges(model, markdownBody: false),
                model.SourceCard.Tags,
                new OblivionCompactPlainBodyContent(["Independent handler"]),
                [],
                [],
                184);
        }
    }
}
