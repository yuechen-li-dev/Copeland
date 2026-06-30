using System.Text.Json;
using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionCardEffectRoutingM12fTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();
    private static readonly StandardTheme Theme = StandardTheme.Default;

    [Fact]
    public void CardActionInvocation_HasStableFields()
    {
        OblivionCardActionInvocation invocation = new(
            new OblivionCardId("card-alpha"),
            "run",
            OblivionWorkbenchCatalog.CardsPageId,
            "cards/card-alpha.card.toml");

        Assert.Equal("card-alpha", invocation.CardId.Value);
        Assert.Equal("run", invocation.ActionId);
        Assert.Equal(OblivionWorkbenchCatalog.CardsPageId, invocation.PageId);
        Assert.Equal("cards/card-alpha.card.toml", invocation.SourcePath);
    }

    [Fact]
    public void CardEffectRequest_HasStableRequestId()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "code-fact-placeholder",
            "run",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        Assert.Equal(
            "oblivion.cards:oblivion-code-fact-card:run:RunCodeFact",
            outcome.Request.RequestId);
    }

    [Fact]
    public void CardEffectResult_IsDeterministic()
    {
        OblivionCardEffectRouter router = new();
        OblivionCardEffectRequest request = new(
            "req-1",
            new OblivionCardId("card-alpha"),
            OblivionCardEffectKind.RunCodeFact,
            "CodeFact:run",
            new Dictionary<string, string>(StringComparer.Ordinal));

        OblivionCardEffectResult first = router.Route(request);
        OblivionCardEffectResult second = router.Route(request);

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Equal(first.CardId, second.CardId);
        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Message, second.Message);
        Assert.Equal(first.Diagnostics.Select(d => d.Code), second.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void CardEffectResult_DeferredByDefault()
    {
        OblivionCardEffectRouter router = new();
        OblivionCardEffectResult result = router.Route(new OblivionCardEffectRequest(
            "req-1",
            new OblivionCardId("card-alpha"),
            OblivionCardEffectKind.RefreshMarkdown,
            "Note:refresh-markdown",
            new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Equal(OblivionCardEffectStatus.Deferred, result.Status);
    }

    [Fact]
    public void MarkdownCardHandler_CreatesRefreshMarkdownEffectRequest()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
            "markdown-first-roadmap",
            "refresh-markdown",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        Assert.Equal(OblivionCardEffectKind.RefreshMarkdown, outcome.Request.Kind);
    }

    [Fact]
    public void CodeFactCardHandler_CreatesRunCodeFactEffectRequest()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "code-fact-placeholder",
            "run",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        Assert.Equal(OblivionCardEffectKind.RunCodeFact, outcome.Request.Kind);
    }

    [Fact]
    public void CodeTheoryCardHandler_CreatesRunCodeTheoryEffectRequest()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "code-theory-placeholder",
            "run-theory",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        Assert.Equal(OblivionCardEffectKind.RunCodeTheory, outcome.Request.Kind);
    }

    [Fact]
    public void ArtifactCardHandler_CreatesOpenArtifactEffectRequest()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "artifact-placeholder",
            "open-artifact",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        Assert.Equal(OblivionCardEffectKind.OpenArtifact, outcome.Request.Kind);
    }

    [Fact]
    public void UiPreviewCardHandler_CreatesRenderPreviewEffectRequest()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "ui-preview-placeholder",
            "render-preview",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        Assert.Equal(OblivionCardEffectKind.RenderPreview, outcome.Request.Kind);
    }

    [Fact]
    public void EffectRouter_RoutesKnownEffectsToDeferredResults()
    {
        OblivionCardEffectRouter router = new();
        OblivionCardEffectResult result = router.Route(new OblivionCardEffectRequest(
            "req-1",
            new OblivionCardId("card-alpha"),
            OblivionCardEffectKind.RunCodeFact,
            "CodeFact:run",
            new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Equal(OblivionCardEffectStatus.Deferred, result.Status);
        Assert.Contains("Code execution deferred to M13+", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectRouter_RejectsUnknownEffectDeterministically()
    {
        OblivionCardEffectRouter router = new();
        OblivionCardEffectResult result = router.Route(new OblivionCardEffectRequest(
            "req-1",
            new OblivionCardId("card-alpha"),
            OblivionCardEffectKind.Custom,
            "Custom:run",
            new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Equal(OblivionCardEffectStatus.Rejected, result.Status);
        Assert.Equal("M12F-REJECTED-UNKNOWN-EFFECT", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void EffectRouter_DoesNotExecuteCodeFact()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "code-fact-placeholder",
            "run",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        Assert.Equal(OblivionCardEffectStatus.Deferred, outcome.Result.Status);
        Assert.Contains("No Roslyn or xUnit execution occurred.", outcome.Result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectRouter_DoesNotMutateFiles()
    {
        string tempDirectory = CreateOutputDirectory();
        string sentinelPath = Path.Combine(tempDirectory, "sentinel.txt");
        File.WriteAllText(sentinelPath, "stable");

        try
        {
            OblivionCardEffectRouter router = new();
            _ = router.Route(new OblivionCardEffectRequest(
                "req-1",
                new OblivionCardId("card-alpha"),
                OblivionCardEffectKind.OpenSource,
                "Note:open-source",
                new Dictionary<string, string>(StringComparer.Ordinal)));

            Assert.Equal("stable", File.ReadAllText(sentinelPath));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void EffectRouter_DoesNotGenerateArtifacts()
    {
        OblivionCardEffectRouter router = new();
        OblivionCardEffectResult result = router.Route(new OblivionCardEffectRequest(
            "req-1",
            new OblivionCardId("card-alpha"),
            OblivionCardEffectKind.ExportCard,
            "Artifact:export",
            new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public void EffectState_StoresLastResultByCardId()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "code-fact-placeholder",
            "run",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithEffectOutcome(outcome.Request, outcome.Result);

        Assert.Equal(outcome.Result, state.EffectState.GetLastResult(new OblivionCardId("oblivion-code-fact-card")));
    }

    [Fact]
    public void EffectState_DoesNotMutateNavigationSelection()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "cards")
            .WithSelectedCard(OblivionWorkbenchCatalog.CardsPageId, "oblivion-code-fact-card");
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "code-fact-placeholder",
            "run",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        PresenterNavigationState next = state.WithEffectOutcome(outcome.Request, outcome.Result);

        Assert.Equal(
            "oblivion-code-fact-card",
            next.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId)));
    }

    [Fact]
    public void EffectState_IsDeterministic()
    {
        OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
            OblivionWorkbenchCatalog.CardsPageId,
            "code-fact-placeholder",
            "run",
            ProofOptions,
            OblivionCardEffectState.Empty);
        Assert.NotNull(outcome);

        OblivionCardEffectState first = OblivionCardEffectState.Empty.WithOutcome(outcome.Request, outcome.Result);
        OblivionCardEffectState second = OblivionCardEffectState.Empty.WithOutcome(outcome.Request, outcome.Result);

        Assert.Equal(
            first.GetLastRequest(new OblivionCardId("oblivion-code-fact-card"))?.RequestId,
            second.GetLastRequest(new OblivionCardId("oblivion-code-fact-card"))?.RequestId);
        Assert.Equal(
            first.GetLastResult(new OblivionCardId("oblivion-code-fact-card"))?.Status,
            second.GetLastResult(new OblivionCardId("oblivion-code-fact-card"))?.Status);
    }

    [Fact]
    public void Inspector_RendersAvailableActions()
    {
        string text = PageText(RenderPageWithActionState("code-fact-placeholder", "run"));

        Assert.Contains("Available actions", text, StringComparison.Ordinal);
        Assert.Contains("run | Run fact | deferred routing", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspector_RendersDeferredEffectResult()
    {
        string text = PageText(RenderPageWithActionState("code-fact-placeholder", "run"));

        Assert.Contains("Effect routing", text, StringComparison.Ordinal);
        Assert.Contains("RunCodeFact -> Deferred", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspector_RendersExecutionDeferredForCodeFact()
    {
        string text = PageText(RenderPageWithActionState("code-fact-placeholder", "run"));

        Assert.Contains("Execution deferred to M13+.", text, StringComparison.Ordinal);
        Assert.Contains("RunCodeFact -> Deferred", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspector_RendersMarkdownRefreshDeferred()
    {
        string text = PageText(RenderExecutionRoadmapPageWithActionState("markdown-first-roadmap", "refresh-markdown"));

        Assert.Contains("RefreshMarkdown -> Deferred", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportPresenter_EffectRoutingMarkdownCard_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-effect-routing-markdown-card.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "docs",
                    SelectedCardId: "doc-copeland-markdown-frontend-m12a"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_EffectRoutingCodeFactDeferred_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-effect-routing-codefact-deferred.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards",
                    SelectedCardId: "code-fact-placeholder",
                    InvokeActionId: "run"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionEffectRoutingManifestJsonPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M12fManifest_RecordsEffectsNonExecutable()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
            (string jsonPath, string textPath) = OblivionWorkbenchCatalog.WriteEffectRoutingManifest(outputDirectory, state, ProofOptions);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));

            Assert.Equal("M12f", document.RootElement.GetProperty("milestone").GetString());
            Assert.False(document.RootElement.GetProperty("effectsExecutable").GetBoolean());
            Assert.True(File.Exists(textPath));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M12f_DoesNotImplementRoslynExecution()
    {
        string source = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("CSharpCompilation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataReference", source, StringComparison.Ordinal);
    }

    [Fact]
    public void M12f_DoesNotImplementXunitExecution()
    {
        string source = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Xunit.Sdk", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FactAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TheoryAttribute", source, StringComparison.Ordinal);
    }

    [Fact]
    public void M12f_DoesNotImplementVisionary()
    {
        Assert.DoesNotContain(Model.Sections, section => string.Equals(section.Id, "visionary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M12f_DoesNotExecuteCardActions()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = PresenterNavigationDispatch.Dispatch(
            state,
            PresenterNavigationActions.InvokeOblivionCardAction(
                OblivionWorkbenchCatalog.CardsPageId,
                "oblivion-code-fact-card",
                "run"),
            Model,
            ProofOptions,
            PresenterNavigationLayout.Default);

        OblivionCardEffectResult? result = next.EffectState.GetLastResult(new OblivionCardId("oblivion-code-fact-card"));
        Assert.NotNull(result);
        Assert.Equal(OblivionCardEffectStatus.Deferred, result.Status);
    }

    private static PresenterPageRenderResult RenderPageWithActionState(string cardAlias, string actionId)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "cards")
            .WithSelectedCard(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.ResolveCardSelectionId(OblivionWorkbenchCatalog.CardsPageId, cardAlias, ProofOptions));
        state = PresenterNavigationDispatch.Dispatch(
            state,
            PresenterNavigationActions.InvokeOblivionCardAction(
                OblivionWorkbenchCatalog.CardsPageId,
                OblivionWorkbenchCatalog.ResolveCardSelectionId(OblivionWorkbenchCatalog.CardsPageId, cardAlias, ProofOptions),
                actionId),
            Model,
            ProofOptions,
            PresenterNavigationLayout.Default);

        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.CardsPageId,
            DemoState.Default,
            Theme,
            ProofOptions,
            PresenterNavigationLayout.Default.ContentVisibleWidth,
            state);
    }

    private static PresenterPageRenderResult RenderExecutionRoadmapPageWithActionState(string cardAlias, string actionId)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "execution-roadmap")
            .WithSelectedCard(OblivionWorkbenchCatalog.ExecutionRoadmapPageId, cardAlias);
        state = PresenterNavigationDispatch.Dispatch(
            state,
            PresenterNavigationActions.InvokeOblivionCardAction(
                OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
                cardAlias,
                actionId),
            Model,
            ProofOptions,
            PresenterNavigationLayout.Default);

        return PresenterNavigationCatalog.RenderPage(
            OblivionWorkbenchCatalog.ExecutionRoadmapPageId,
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
        string root = Path.Combine([GetRepositoryRoot(), .. segments]);
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string CreateOutputDirectory()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-m12f-tests", Guid.NewGuid().ToString("N"));
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
