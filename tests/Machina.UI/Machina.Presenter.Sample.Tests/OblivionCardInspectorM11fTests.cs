using System.Reflection;
using Machina.Core.Actions;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class OblivionCardInspectorM11fTests
{
    private static readonly PresenterNavigationModel Model = PresenterNavigationCatalog.CreateModel();
    private static readonly PresenterProofOptions ProofOptions = new();
    private static readonly StandardTheme Theme = StandardTheme.Default;

    [Fact]
    public void OblivionSelection_DefaultSelectsFirstCardOrEmptyInspector()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "cards");
        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId);

        Assert.Equal(cards[0].Id.Value, state.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, cards));
    }

    [Fact]
    public void OblivionSelection_SelectCard_StoresSelectionByPage()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = Dispatch(
            state,
            PresenterNavigationActions.SelectOblivionCard(OblivionWorkbenchCatalog.CardsPageId, "oblivion-code-fact-card"));

        Assert.Equal(
            "oblivion-code-fact-card",
            next.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId)));
    }

    [Fact]
    public void OblivionSelection_SwitchPage_RestoresPageSelection()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        state = Dispatch(state, PresenterNavigationActions.SelectOblivionCard(OblivionWorkbenchCatalog.CardsPageId, "oblivion-code-fact-card"));
        state = Dispatch(state, PresenterNavigationActions.SelectOblivionCard(OblivionWorkbenchCatalog.ArtifactsPageId, "oblivion-artifacts-export-policy-card"));

        Assert.Equal("oblivion-code-fact-card", state.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId)));
        Assert.Equal("oblivion-artifacts-export-policy-card", state.GetSelectedCardId(OblivionWorkbenchCatalog.ArtifactsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.ArtifactsPageId)));
    }

    [Fact]
    public void OblivionSelection_MissingSelectedCard_FallsBackDeterministically()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedCard(OblivionWorkbenchCatalog.CardsPageId, "missing-card");
        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId);

        Assert.Equal(cards[0].Id.Value, state.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, cards));
    }

    [Fact]
    public void OblivionSelection_ClearSelection_ShowsEmptyInspector()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        state = Dispatch(state, PresenterNavigationActions.ClearOblivionCardSelection(OblivionWorkbenchCatalog.CardsPageId));

        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId, state);
        string text = PageText(page);

        Assert.Contains("No card selected", text, StringComparison.Ordinal);
        Assert.Null(state.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId)));
    }

    [Fact]
    public void OblivionActions_SelectCard_DispatchesExplicitAction()
    {
        UiActionId actionId = PresenterNavigationActions.SelectOblivionCard(OblivionWorkbenchCatalog.CardsPageId, "oblivion-code-fact-card");

        Assert.True(PresenterNavigationActions.TryParseSelectOblivionCard(actionId, out string pageId, out string cardId));
        Assert.Equal(OblivionWorkbenchCatalog.CardsPageId, pageId);
        Assert.Equal("oblivion-code-fact-card", cardId);
    }

    [Fact]
    public void OblivionActions_CardActionsRemainMetadataOnly()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-code-fact-card");

        Assert.Contains("Available actions", PageText(page), StringComparison.Ordinal);
        Assert.Contains("oblivion-code-fact-card", PageText(page), StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionActions_NoExecutionOccursOnActionClick()
    {
        string source = string.Join(
            Environment.NewLine,
            GetSourceFiles("samples", "Machina.UI", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.CodeAnalysis", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Xunit.Sdk", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionHitTest_HitsCardByBounds()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId);
        OblivionCardHitTarget target = Assert.Single(
            page.OblivionInteraction!.CardTargets,
            candidate => candidate.CardId == "oblivion-intro-note-card");

        UiAction? action = page.OblivionInteraction!.HitTest(Center(target.Bounds), scrollOffset: 0);

        Assert.NotNull(action);
        bool parsed = PresenterNavigationActions.TryParseToggleOblivionCardExpansion(action!.Id, out _, out string cardId) ||
            PresenterNavigationActions.TryParseSelectOblivionCard(action.Id, out _, out cardId);
        Assert.True(parsed);
        Assert.Equal("oblivion-intro-note-card", cardId);
    }

    [Fact]
    public void OblivionHitTest_AccountsForScrollOffset()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId);
        OblivionCardHitTarget target = Assert.Single(
            page.OblivionInteraction!.CardTargets,
            candidate => candidate.CardId == "oblivion-code-fact-card");
        double scrollOffset = target.Bounds.Y - 24;
        PresenterInputPoint viewportPoint = new((float)(target.Bounds.X + 32), 24);

        UiAction? action = page.OblivionInteraction!.HitTest(viewportPoint, scrollOffset);

        Assert.NotNull(action);
        bool parsed = PresenterNavigationActions.TryParseToggleOblivionCardExpansion(action!.Id, out _, out string cardId) ||
            PresenterNavigationActions.TryParseSelectOblivionCard(action.Id, out _, out cardId);
        Assert.True(parsed);
        Assert.Equal("oblivion-code-fact-card", cardId);
    }

    [Fact]
    public void OblivionHitTest_ClickOutsideCardsReturnsNone()
    {
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId);

        Assert.Null(page.OblivionInteraction!.HitTest(new PresenterInputPoint(700, 40), scrollOffset: 0));
    }

    [Fact]
    public void OblivionHitTest_DoesNotReferenceAvaloniaTypes()
    {
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(OblivionCardHitTarget));
        AssertTypeSurfaceDoesNotReferenceAvalonia(typeof(OblivionPageInteractionMap));
    }

    [Fact]
    public void OblivionInspector_RendersSelectedCardTitleKindStatus()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-code-fact-card");
        string text = PageText(page);

        Assert.Contains("Code fact placeholder", text, StringComparison.Ordinal);
        Assert.Contains("Kind: Code Fact", text, StringComparison.Ordinal);
        Assert.Contains("Status: Deferred", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersTagsAndBody()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-code-fact-card");
        string text = PageText(page);

        Assert.Contains("Tags: code, fact, deferred", text, StringComparison.Ordinal);
        Assert.Contains("[Fact]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersSourcePathAndCardId()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-code-fact-card");
        string text = PageText(page);
        OblivionCard selectedCard = Assert.Single(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId),
            card => card.Id.Value == "oblivion-code-fact-card");

        Assert.Contains("Card ID: oblivion-code-fact-card", text, StringComparison.Ordinal);
        Assert.Equal("cards/code-fact-placeholder.card.toml", selectedCard.SourcePath);
        Assert.Contains("Metadata", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersActionsAsMetadata()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-code-fact-card");
        string text = PageText(page);

        Assert.Contains("run | Run fact | deferred routing", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersArtifactsAsMetadata()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-artifact-placeholder-card");
        string text = PageText(page);
        OblivionCard selectedCard = Assert.Single(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId),
            card => card.Id.Value == "oblivion-artifact-placeholder-card");

        Assert.Contains("Artifacts metadata", text, StringComparison.Ordinal);
        Assert.Contains(selectedCard.Artifacts, artifact => artifact.Id == "workspace-manifest");
    }

    [Fact]
    public void OblivionInspector_RendersExecutionDeferredNotice()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-code-fact-card");
        string text = PageText(page);

        Assert.Contains("Effect routing skeleton only.", text, StringComparison.Ordinal);
        Assert.Contains("Effect routing", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionInspector_RendersEmptyStateWhenNoSelection()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .ClearSelectedCard(OblivionWorkbenchCatalog.CardsPageId);
        PresenterPageRenderResult page = RenderPage(OblivionWorkbenchCatalog.CardsPageId, state);

        Assert.Contains("No card selected", PageText(page), StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionPersistedCard_SourcePathAppearsInInspector()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-intro-note-card");
        Assert.Contains("cards/intro.card.toml", PageText(page), StringComparison.Ordinal);
    }

    [Fact]
    public void OblivionWorkspaceLoadedCards_CanBeSelected()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model);
        PresenterNavigationState next = Dispatch(state, PresenterNavigationActions.SelectOblivionCard(OblivionWorkbenchCatalog.CardsPageId, "code-fact-placeholder"));

        Assert.Equal("oblivion-code-fact-card", next.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId)));
    }

    [Fact]
    public void OblivionWorkspaceReloadMissingSelection_FallsBack()
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedCard(OblivionWorkbenchCatalog.CardsPageId, "missing");

        PresenterNavigationShellRenderResult render = PresenterNavigationShellRenderer.Render(
            DemoState.Default,
            state.WithSelectedSection("oblivion").WithSelectedTab("oblivion", "cards"),
            Theme,
            ProofOptions);

        Assert.Equal(
            "oblivion-intro-note-card",
            render.NavigationState.GetSelectedCardId(OblivionWorkbenchCatalog.CardsPageId, OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId)));
    }

    [Fact]
    public void ExportPresenter_OblivionInspectorIntro_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-card-inspector-intro.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards",
                    SelectedCardId: "intro"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(File.Exists(result.OblivionInspectorManifestJsonPath!));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_OblivionInspectorCodeFact_WritesArtifact()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-card-inspector-code-fact.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "cards",
                    SelectedCardId: "code-fact-placeholder"),
                Theme);

            Assert.True(File.Exists(result.OutputPath));
            Assert.Contains("oblivion.cards:oblivion-code-fact-card", File.ReadAllText(result.OblivionInspectorManifestTextPath!), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void ExportPresenter_OblivionInspectorManifest_WritesJsonAndText()
    {
        string outputDirectory = CreateOutputDirectory();

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                Path.Combine(outputDirectory, "presenter-oblivion-card-inspector-artifact.png"),
                ProofOptions,
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "oblivion",
                    SelectedTabId: "artifacts"),
                Theme);

            Assert.True(File.Exists(result.OblivionInspectorManifestJsonPath!));
            Assert.True(File.Exists(result.OblivionInspectorManifestTextPath!));
            Assert.Contains("\"milestone\": \"M12f\"", File.ReadAllText(result.OblivionInspectorManifestJsonPath!), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void M11f_DoesNotReferenceRoslynExecution()
    {
        string combinedText = string.Join(Environment.NewLine, GetSourceFiles("samples", "Machina.UI", "Machina.Presenter.Sample").Select(File.ReadAllText));

        Assert.DoesNotContain("Microsoft.CodeAnalysis", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpCompilation", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("MetadataReference", combinedText, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyLoadContext", combinedText, StringComparison.Ordinal);
    }

    [Fact]
    public void M11f_DoesNotRunFactOrTheoryCards()
    {
        PresenterPageRenderResult page = RenderSelectedPage("oblivion-code-fact-card");
        string text = PageText(page);
        OblivionCard selectedCard = Assert.Single(
            OblivionWorkbenchCatalog.GetPageCardsForSelection(OblivionWorkbenchCatalog.CardsPageId),
            card => card.Id.Value == "oblivion-code-fact-card");

        Assert.Contains("Effect routing skeleton only.", text, StringComparison.Ordinal);
        Assert.Contains("Effect routing", text, StringComparison.Ordinal);
        Assert.Equal(OblivionCardStatus.Deferred, selectedCard.Status);
    }

    [Fact]
    public void M11f_DoesNotImplementVisionaryEditor()
    {
        Assert.DoesNotContain(Model.Sections, section => section.Id == "visionary");
        Assert.DoesNotContain(GetSourceFiles("samples", "Machina.UI", "Machina.Presenter.Sample").Select(File.ReadAllText), source => source.Contains("VisionaryEditor", StringComparison.Ordinal));
    }

    private static PresenterPageRenderResult RenderSelectedPage(string cardId)
    {
        PresenterNavigationState state = PresenterNavigationState.CreateDefault(Model)
            .WithSelectedSection("oblivion")
            .WithSelectedTab("oblivion", "cards")
            .WithSelectedCard(OblivionWorkbenchCatalog.CardsPageId, cardId);
        return RenderPage(OblivionWorkbenchCatalog.CardsPageId, state);
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

    private static PresenterNavigationState Dispatch(PresenterNavigationState state, UiActionId actionId)
    {
        return PresenterNavigationDispatch.Dispatch(
            state,
            actionId,
            Model,
            ProofOptions,
            PresenterNavigationLayout.Default);
    }

    private static string PageText(PresenterPageRenderResult page)
    {
        return string.Join(
            Environment.NewLine,
            page.Frame.RenderCommands
                .OfType<DrawTextCommand>()
                .Select(command => command.Text));
    }

    private static PresenterInputPoint Center(Rect rect)
    {
        return new PresenterInputPoint(
            (float)(rect.X + (rect.Width / 2)),
            (float)(rect.Y + (rect.Height / 2)));
    }

    private static void AssertTypeSurfaceDoesNotReferenceAvalonia(Type type)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            AssertNoAvaloniaType(property.PropertyType);
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            AssertNoAvaloniaType(field.FieldType);
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                AssertNoAvaloniaType(parameter.ParameterType);
            }
        }

        foreach (MethodInfo method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            AssertNoAvaloniaType(method.ReturnType);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                AssertNoAvaloniaType(parameter.ParameterType);
            }
        }
    }

    private static void AssertNoAvaloniaType(Type type)
    {
        if (type == typeof(void))
        {
            return;
        }

        Assert.False(
            type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true,
            $"Unexpected Avalonia type reference: {type.FullName}");

        if (type.IsArray)
        {
            AssertNoAvaloniaType(type.GetElementType()!);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (Type genericArgument in type.GetGenericArguments())
            {
                AssertNoAvaloniaType(genericArgument);
            }
        }
    }

    private static IEnumerable<string> GetSourceFiles(params string[] segments)
    {
        string[] pathParts = new string[segments.Length + 1];
        pathParts[0] = GetRepositoryRoot();
        Array.Copy(segments, 0, pathParts, 1, segments.Length);
        string directory = Path.Combine(pathParts);
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-presenter-m11f-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
