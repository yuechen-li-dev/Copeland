using System.Text.Json;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Theme;
using static Machina.Presenter.Sample.OblivionMarkdownBody;

namespace Machina.Presenter.Sample;

public static class OblivionWorkbenchCatalog
{
    private static readonly OblivionCardHandlerRegistry CardHandlers = OblivionCardHandlerRegistry.CreateDefault();

    public const string CardsPageId = "oblivion.cards";
    public const string ExecutionRoadmapPageId = "oblivion.execution-roadmap";
    public const string ArtifactsPageId = "oblivion.artifacts";
    public const string DocsPageId = OblivionDocsDogfoodCatalog.PageId;

    public static IReadOnlyList<OblivionCard> CreateCardsPageCards()
    {
        return GetPageCards(CardsPageId, proofOptions: null);
    }

    public static IReadOnlyList<OblivionCard> CreateExecutionRoadmapCards()
    {
        return GetPageCards(ExecutionRoadmapPageId, proofOptions: null);
    }

    public static IReadOnlyList<OblivionCard> CreateArtifactsPageCards()
    {
        return GetPageCards(ArtifactsPageId, proofOptions: null);
    }

    public static IReadOnlyList<OblivionCard> CreateDocsPageCards()
    {
        return GetPageCards(DocsPageId, proofOptions: null);
    }

    public static IReadOnlyList<OblivionCard> CreateAllCards(PresenterProofOptions? proofOptions = null)
    {
        return
        [
            .. GetPageCards(CardsPageId, proofOptions),
            .. GetPageCards(ExecutionRoadmapPageId, proofOptions),
            .. GetPageCards(ArtifactsPageId, proofOptions),
            .. GetPageCards(DocsPageId, proofOptions),
        ];
    }

    public static IReadOnlyList<UiRow> BuildPageRows(
        string pageId,
        StandardTheme theme,
        int contentWidth,
        int viewportHeight,
        PresenterProofOptions? proofOptions = null,
        PresenterNavigationState? navigationState = null,
        PresenterShellMode shellMode = PresenterShellMode.Wide)
    {
        ArgumentNullException.ThrowIfNull(theme);

        OblivionCardEffectState effectState = navigationState?.EffectState ?? OblivionCardEffectState.Empty;
        IReadOnlyList<OblivionBuiltCard> cards = GetBuiltPageCards(pageId, proofOptions, effectState, navigationState);
        OblivionInspectorSelection selection = ResolveSelection(pageId, cards, navigationState);
        bool compactInspector = shellMode == PresenterShellMode.Compact &&
            navigationState?.CompactPane == PresenterCompactPane.Inspector;
        OblivionPageLayout layout = shellMode == PresenterShellMode.Compact
            ? OblivionPageLayout.CreateCompact(contentWidth, viewportHeight)
            : OblivionPageLayout.CreateWide(contentWidth, viewportHeight);

        if (shellMode == PresenterShellMode.Compact)
        {
            return compactInspector
                ? BuildCompactInspectorRows(pageId, selection, theme, layout)
                : BuildCompactCardListRows(pageId, cards, selection, theme, layout);
        }

        List<UiRow> rows = [];
        double currentTop = 0;

        rows.Add(
            Row.Anchor(
                id: $"{pageId}.cards-panel",
                parent: "root",
                left: 0,
                top: 0,
                width: layout.CardsColumnWidth,
                height: Math.Max(160, GetCardsColumnHeight(cards, layout)),
                view: View.Rect(
                    background: ColorToken.Hex(0x00000000),
                    borderColor: null,
                    borderThickness: 0)));

        rows.Add(
            Row.Anchor(
                id: $"{pageId}.inspector-panel",
                parent: "root",
                left: layout.InspectorLeft,
                top: 0,
                width: layout.InspectorWidth,
                height: GetInspectorHeight(selection),
                view: View.Rect(
                    background: ColorToken.Hex(0x00000000),
                    borderColor: null,
                    borderThickness: 0)));

        foreach (OblivionBuiltCard builtCard in cards)
        {
            double cardHeight = GetRenderedCardHeight(builtCard.CompactView, layout);
            bool isSelected = string.Equals(selection.SelectedCardId, builtCard.SourceCard.Id.Value, StringComparison.Ordinal);
            rows.Add(
                Row.Anchor(
                    builtCard.SourceCard.Id.Value + ".anchor",
                    "root",
                    left: 0,
                    top: currentTop,
                    width: layout.CardsColumnWidth,
                    height: cardHeight,
                    component: OblivionCardRenderer.BuildCard(
                        builtCard.CompactView,
                        theme,
                        new OblivionCardRenderOptions(
                            Width: layout.CardsColumnWidth,
                            Height: cardHeight),
                        isSelected)));

            currentTop += cardHeight + 24;
        }

        rows.AddRange(BuildInspectorRows(pageId, selection, theme, layout));

        return rows;
    }

    public static double GetPageContentHeight(
        string pageId,
        PresenterProofOptions? proofOptions = null,
        PresenterNavigationState? navigationState = null,
        int viewportHeight = 596)
    {
        IReadOnlyList<OblivionBuiltCard> cards = GetBuiltPageCards(pageId, proofOptions, OblivionCardEffectState.Empty, navigationState);
        OblivionPageLayout layout = OblivionPageLayout.CreateWide(contentWidth: 920, viewportHeight);
        return Math.Max(GetCardsColumnHeight(cards, layout), 1440);
    }

    public static double GetCardHeight(OblivionCard card)
    {
        return CardHandlers.BuildCard(card, effectState: OblivionCardEffectState.Empty).CompactView.PreferredHeight;
    }

    public static OblivionWorkspaceLoadResult LoadWorkspace(PresenterProofOptions? proofOptions = null, bool useCache = true)
    {
        string manifestPath = OblivionWorkspacePaths.ResolveWorkspaceManifestPath(proofOptions?.OblivionWorkspacePath);
        return OblivionWorkspaceLoader.Load(manifestPath, useCache: useCache);
    }

    public static (string jsonPath, string textPath) WriteManifest(
        string outputDirectory,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-workspace-persistence-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-workspace-persistence-manifest.txt");

        string workspacePath = OblivionWorkspacePaths.ResolveWorkspaceManifestPath(proofOptions?.OblivionWorkspacePath);
        bool usingFallbackCatalog = ShouldUseFallbackCatalog(proofOptions);
        OblivionWorkspaceLoadResult loadResult = usingFallbackCatalog
            ? new OblivionWorkspaceLoadResult(null, [])
            : LoadWorkspace(proofOptions);

        IReadOnlyList<OblivionCard> cards = CreateAllCards(proofOptions)
            .OrderBy(card => card.Id.Value, StringComparer.Ordinal)
            .ToArray();

        string[] cardKinds = cards
            .Select(card => OblivionWorkspaceValidator.GetCardKindValue(card.Kind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        string[] statuses = cards
            .Select(card => OblivionWorkspaceValidator.GetCardStatusValue(card.Status))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        int sectionsLoaded = usingFallbackCatalog
            ? 1
            : loadResult.Workspace?.Sections.Count ?? 0;
        int pagesLoaded = usingFallbackCatalog
            ? 4
            : loadResult.Workspace?.Sections.Sum(section => section.Pages.Count) ?? 0;
        int cardsLoaded = cards.Count;
        string[] validationErrors = usingFallbackCatalog
            ? []
            : loadResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity == OblivionWorkspaceDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
                .ToArray();

        string[] deferredWork =
        [
            "Roslyn execution",
            "xUnit [Fact] and [Theory] runtime",
            "Artifact execution and generation runtime",
            "Visionary code editor/source workspace implementation",
            "Markdown editing",
        ];

        var manifest = new
        {
            milestone = "M11d",
            kind = "oblivion-workspace-persistence",
            rootFormat = "json",
            assetFormat = "toml",
            workspacePath,
            sectionsLoaded,
            pagesLoaded,
            cardsLoaded,
            cardKinds,
            statuses,
            validationErrors,
            executionEnabled = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            usingFallbackCatalog,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M11d",
            "kind=oblivion-workspace-persistence",
            "rootFormat=json",
            "assetFormat=toml",
            $"workspacePath={workspacePath}",
            $"sectionsLoaded={sectionsLoaded}",
            $"pagesLoaded={pagesLoaded}",
            $"cardsLoaded={cardsLoaded}",
            $"cardKinds={string.Join(",", cardKinds)}",
            $"statuses={string.Join(",", statuses)}",
            $"validationErrors={string.Join(" | ", validationErrors)}",
            "executionEnabled=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"usingFallbackCatalog={usingFallbackCatalog.ToString().ToLowerInvariant()}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
            "cardsRendered:",
            .. cards.Select(card => $"  {card.Id.Value}:{OblivionWorkspaceValidator.GetCardKindValue(card.Kind)}:{OblivionWorkspaceValidator.GetCardStatusValue(card.Status)}:{card.Title}"),
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteInspectorManifest(
        string outputDirectory,
        PresenterNavigationState navigationState,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(navigationState);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-card-inspector-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-card-inspector-manifest.txt");

        string[] pageIds =
        [
            CardsPageId,
            ExecutionRoadmapPageId,
            ArtifactsPageId,
            DocsPageId,
        ];

        string[] exportedSelections = pageIds
            .Select(pageId =>
            {
                IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
                return $"{pageId}:{navigationState.GetSelectedCardId(pageId, cards) ?? "<none>"}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] routedCards = navigationState.EffectState.LastResultByCardId.Keys
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        string[] deferredWork = GetPhaseCloseoutDeferredWork();

        var manifest = new
        {
            milestone = "M12f",
            kind = "oblivion-card-selection-inspector",
            selectionModel = "page-local-selected-card-id-by-page-id",
            inspectorEnabled = true,
            defaultSelectionPolicy = "first-card-when-no-explicit-selection; empty-when-cleared",
            selectedCardsExported = exportedSelections,
            effectRoutingVisible = true,
            routedCards,
            actionsExecutable = false,
            artifactsExecutable = false,
            effectsExecutable = false,
            executionEnabled = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M12f",
            "kind=oblivion-card-selection-inspector",
            "selectionModel=page-local-selected-card-id-by-page-id",
            "inspectorEnabled=true",
            "defaultSelectionPolicy=first-card-when-no-explicit-selection; empty-when-cleared",
            $"selectedCardsExported={string.Join(",", exportedSelections)}",
            "effectRoutingVisible=true",
            $"routedCards={string.Join(",", routedCards)}",
            "actionsExecutable=false",
            "artifactsExecutable=false",
            "effectsExecutable=false",
            "executionEnabled=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteExpandableMarkdownCardsManifest(
        string outputDirectory,
        PresenterNavigationState navigationState,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(navigationState);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-expandable-markdown-cards-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-expandable-markdown-cards-manifest.txt");

        string[] proofArtifacts =
        [
            "artifacts/m15c/m15c-oblivion-docs-collapsed-1280x720.png",
            "artifacts/m15c/m15c-oblivion-docs-expanded-1280x720.png",
            "artifacts/m15c/m15c-oblivion-docs-expanded-scrolled-1280x720.png",
            "artifacts/m15c/m15c-oblivion-cards-expanded-1280x720.png",
            "artifacts/m15c/m15c-oblivion-docs-compact-expanded-960x540.png",
            "artifacts/m15c/m15c-oblivion-inspector-after-expand-1280x720.png",
            "artifacts/m15c/oblivion-expandable-markdown-cards-manifest.json",
            "artifacts/m15c/oblivion-expandable-markdown-cards-manifest.txt",
        ];
        string[] deferredWork =
        [
            "Markdown editing",
            "Notebook execution",
            "Roslyn execution",
            "Pointer-drag local scrollbar",
            "Aurelian work",
            "VD-MIR work",
        ];

        var manifest = new
        {
            milestone = "M15c",
            kind = "oblivion-expandable-markdown-cards",
            expandableCardsImplemented = true,
            markdownBodyInlineInStack = true,
            inspectorPrimaryBodySurface = false,
            selectionExpansionSeparated = true,
            localBodyScrollImplemented = true,
            collapsedCardScannable = true,
            expandedCardReadable = true,
            markdownEditingImplemented = false,
            notebookExecutionImplemented = false,
            roslynExecutionImplemented = false,
            aurelianWorkPerformed = false,
            vdMirWorkPerformed = false,
            arbitrary2DLayoutSolverImplemented = false,
            validationStatus = "implemented",
            proofArtifacts,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M15c",
            "kind=oblivion-expandable-markdown-cards",
            "expandableCardsImplemented=true",
            "markdownBodyInlineInStack=true",
            "inspectorPrimaryBodySurface=false",
            "selectionExpansionSeparated=true",
            "localBodyScrollImplemented=true",
            "collapsedCardScannable=true",
            "expandedCardReadable=true",
            "markdownEditingImplemented=false",
            "notebookExecutionImplemented=false",
            "roslynExecutionImplemented=false",
            "aurelianWorkPerformed=false",
            "vdMirWorkPerformed=false",
            "arbitrary2DLayoutSolverImplemented=false",
            "validationStatus=implemented",
            $"proofArtifacts={string.Join(" | ", proofArtifacts)}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteExpandedMarkdownReadingSurfaceManifest(
        string outputDirectory,
        PresenterNavigationState navigationState,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(navigationState);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-expanded-markdown-reading-surface-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-expanded-markdown-reading-surface-manifest.txt");

        string[] proofArtifacts =
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
        string[] deferredWork =
        [
            "TOML loading for markdown reading style",
            "Markdown editing",
            "Notebook execution",
            "Roslyn execution",
            "Pointer-drag inspector raw-source scroll",
            "Aurelian work",
            "VD-MIR work",
        ];

        var manifest = new
        {
            milestone = "M15d",
            kind = "oblivion-expanded-markdown-reading-surface",
            expandedMarkdownReadableContrast = true,
            markdownReadingStyleRecordImplemented = true,
            styleTomlLoadingImplemented = false,
            styleTomlLoadingDeferred = true,
            expandedCardUsesDocumentHeight = true,
            singleExpandedCardPerPage = true,
            localBodyScrollPreserved = true,
            inspectorRendersMarkdownBody = false,
            inspectorShowsRawMarkdownSource = true,
            inspectorRawSourceScrollable = true,
            markdownEditingImplemented = false,
            notebookExecutionImplemented = false,
            roslynExecutionImplemented = false,
            aurelianWorkPerformed = false,
            vdMirWorkPerformed = false,
            arbitrary2DLayoutSolverImplemented = false,
            validationStatus = "implemented",
            proofArtifacts,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M15d",
            "kind=oblivion-expanded-markdown-reading-surface",
            "expandedMarkdownReadableContrast=true",
            "markdownReadingStyleRecordImplemented=true",
            "styleTomlLoadingImplemented=false",
            "styleTomlLoadingDeferred=true",
            "expandedCardUsesDocumentHeight=true",
            "singleExpandedCardPerPage=true",
            "localBodyScrollPreserved=true",
            "inspectorRendersMarkdownBody=false",
            "inspectorShowsRawMarkdownSource=true",
            "inspectorRawSourceScrollable=true",
            "markdownEditingImplemented=false",
            "notebookExecutionImplemented=false",
            "roslynExecutionImplemented=false",
            "aurelianWorkPerformed=false",
            "vdMirWorkPerformed=false",
            "arbitrary2DLayoutSolverImplemented=false",
            "validationStatus=implemented",
            $"proofArtifacts={string.Join(" | ", proofArtifacts)}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WritePhaseCloseoutManifest(
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-phase-closeout-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-phase-closeout-manifest.txt");

        string[] canonicalCommands =
        [
            @".\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-closeout-status.png -SelectedSection oblivion -SelectedTab cards -SelectedCard oblivion-substrate-status",
            @".\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-markdown-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard markdown-first-roadmap",
            @".\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-execution-deferred.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard execution-deferred",
            @".\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-visionary-future.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard visionary-future",
        ];

        string[] deferredWork = GetPhaseCloseoutDeferredWork();

        var manifest = new
        {
            milestone = "M11g",
            kind = "oblivion-phase-closeout",
            substrateReady = true,
            workspacePersistenceReady = true,
            inspectorReady = true,
            markdownNext = true,
            markdownImplemented = false,
            executionEnabled = false,
            roslynEnabled = false,
            xunitEnabled = false,
            factExecutionDeferredUntil = "M13+",
            visionaryImplemented = false,
            canonicalCommands,
            deferredWork,
            nextPhase = "M12 Markdown document/card support",
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M11g",
            "kind=oblivion-phase-closeout",
            "substrateReady=true",
            "workspacePersistenceReady=true",
            "inspectorReady=true",
            "markdownNext=true",
            "markdownImplemented=false",
            "executionEnabled=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "factExecutionDeferredUntil=M13+",
            "visionaryImplemented=false",
            $"canonicalCommands={string.Join(" | ", canonicalCommands)}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
            "nextPhase=M12 Markdown document/card support",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static IReadOnlyList<OblivionCard> GetPageCardsForSelection(
        string pageId,
        PresenterProofOptions? proofOptions = null)
    {
        return GetPageCards(pageId, proofOptions);
    }

    public static IReadOnlyList<OblivionBuiltCard> GetBuiltPageCardsForSelection(
        string pageId,
        PresenterProofOptions? proofOptions = null,
        OblivionCardEffectState? effectState = null,
        PresenterNavigationState? navigationState = null)
    {
        return GetBuiltPageCards(pageId, proofOptions, effectState ?? OblivionCardEffectState.Empty, navigationState);
    }

    public static string ResolveCardSelectionId(
        string pageId,
        string requestedCardId,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(requestedCardId);

        IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
        OblivionCard? exact = cards.FirstOrDefault(card => string.Equals(card.Id.Value, requestedCardId, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact.Id.Value;
        }

        string normalized = requestedCardId.Trim();
        OblivionCard? alias = cards.FirstOrDefault(card =>
            string.Equals(card.SourcePath is null ? null : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(card.SourcePath)), normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(card.SourcePath is null ? null : Path.GetFileNameWithoutExtension(card.SourcePath), normalized, StringComparison.OrdinalIgnoreCase));
        return alias?.Id.Value ?? requestedCardId;
    }

    public static OblivionPageInteractionMap BuildInteractionMap(
        string pageId,
        PresenterProofOptions? proofOptions,
        ResolvedLayoutDocument resolved,
        PresenterNavigationState? navigationState,
        PresenterShellMode shellMode)
    {
        IReadOnlyList<OblivionBuiltCard> cards = GetBuiltPageCards(
            pageId,
            proofOptions,
            navigationState?.EffectState ?? OblivionCardEffectState.Empty,
            navigationState);

        if (shellMode == PresenterShellMode.Compact &&
            navigationState?.CompactPane == PresenterCompactPane.Inspector)
        {
            return new OblivionPageInteractionMap(pageId, [], []);
        }

        List<OblivionCardHitTarget> cardTargets = [];
        List<OblivionCardBodyHitTarget> bodyTargets = [];

        foreach (OblivionBuiltCard builtCard in cards)
        {
            string cardId = builtCard.SourceCard.Id.Value;
            PresenterCardFrame frame = OblivionCardRenderer.DescribeFrame(resolved, cardId);
            Rect headerBounds = OblivionCardRenderer.DescribeHeaderHitRect(resolved, cardId);
            UiActionId toggleAction = PresenterNavigationActions.ToggleOblivionCardExpansion(pageId, cardId);

            cardTargets.Add(new OblivionCardHitTarget(pageId, cardId, frame.Bounds, toggleAction, headerBounds));

            OblivionExpandedBodyViewport? viewport = OblivionCardRenderer.DescribeExpandedBodyViewport(
                resolved,
                builtCard.CompactView,
                cardId);
            if (viewport is not null)
            {
                bodyTargets.Add(
                    new OblivionCardBodyHitTarget(
                        pageId,
                        cardId,
                        viewport.Bounds,
                        PresenterNavigationActions.SelectOblivionCard(pageId, cardId),
                        viewport.ScrollbarGeometry,
                        viewport.ContentHeight));
            }
        }

        return new OblivionPageInteractionMap(pageId, cardTargets, bodyTargets);
    }

    public static double ClampBodyScrollOffset(
        string pageId,
        string cardId,
        double requestedOffset,
        PresenterProofOptions? proofOptions,
        PresenterNavigationState navigationState,
        PresenterNavigationLayout layout)
    {
        IReadOnlyList<OblivionBuiltCard> cards = GetBuiltPageCards(
            pageId,
            proofOptions,
            navigationState.EffectState,
            navigationState);
        OblivionBuiltCard? builtCard = cards.FirstOrDefault(card =>
            string.Equals(card.SourceCard.Id.Value, cardId, StringComparison.Ordinal));
        if (builtCard is null || builtCard.CompactView.Body is not OblivionCompactMarkdownBodyContent markdownBody)
        {
            return 0;
        }

        int contentWidth = layout.ContentVisibleWidth;
        OblivionPageLayout pageLayout = layout.ShellMode == PresenterShellMode.Compact
            ? OblivionPageLayout.CreateCompact(contentWidth, layout.ViewportHeight)
            : OblivionPageLayout.CreateWide(contentWidth, layout.ViewportHeight);
        double cardWidth = pageLayout.CardsColumnWidth;
        double cardHeight = GetRenderedCardHeight(builtCard.CompactView, pageLayout);
        OblivionCardRenderOptions renderOptions = new(cardWidth, cardHeight);
        double bodyTop = OblivionCardRenderer.ComputeBodyTop(builtCard.CompactView, renderOptions);
        PresenterCardLayout cardLayout = OblivionCardRenderer.ComputeLayout(
            builtCard.CompactView,
            renderOptions,
            StandardTheme.Default.Card.Default,
            bodyTop);
        double viewportHeight = Math.Max(120, cardLayout.BodyHeight);
        double contentHeight = OblivionMarkdownRenderer.MeasureExpandedContentHeight(markdownBody.Body, cardLayout.BodyWidth);
        return PresenterScrollRegion.ClampScrollOffset(contentHeight, viewportHeight, requestedOffset);
    }

    public static OblivionCardEffectOutcome? InvokeCardAction(
        string pageId,
        string cardId,
        string actionId,
        PresenterProofOptions? proofOptions = null,
        OblivionCardEffectState? effectState = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
        string resolvedCardId = ResolveCardSelectionId(pageId, cardId, proofOptions);
        OblivionCard? card = cards.FirstOrDefault(candidate =>
            string.Equals(candidate.Id.Value, resolvedCardId, StringComparison.Ordinal));
        if (card is null)
        {
            return null;
        }

        return CardHandlers.InvokeAction(
            card,
            pageId,
            actionId,
            card.WorkspaceId,
            effectState ?? OblivionCardEffectState.Empty);
    }

    private static IReadOnlyList<OblivionBuiltCard> GetBuiltPageCards(
        string pageId,
        PresenterProofOptions? proofOptions,
        OblivionCardEffectState effectState,
        PresenterNavigationState? navigationState = null)
    {
        return GetPageCards(pageId, proofOptions)
            .Select(card => CardHandlers.BuildCard(
                card,
                pageId,
                card.WorkspaceId,
                effectState,
                ResolveLocalState(pageId, card, navigationState)))
            .ToArray();
    }

    private static IReadOnlyList<OblivionCard> GetPageCards(string pageId, PresenterProofOptions? proofOptions)
    {
        if (ShouldUseFallbackCatalog(proofOptions))
        {
            return GetFallbackPageCards(pageId);
        }

        OblivionWorkspaceLoadResult loadResult = LoadWorkspace(proofOptions);
        if (!loadResult.Succeeded || loadResult.Workspace is null)
        {
            return CreateWorkspaceErrorCards(pageId, loadResult, proofOptions);
        }

        OblivionWorkspacePage? page = loadResult.Workspace.Sections
            .SelectMany(section => section.Pages)
            .FirstOrDefault(candidate => string.Equals(candidate.PresenterPageId, pageId, StringComparison.Ordinal));

        if (page is null)
        {
            return
            [
                CreateStatusCard(
                    "oblivion-missing-page-card",
                    OblivionCardStatus.Failing,
                    "Oblivion page missing",
                    "Workspace did not contain the requested page",
                    ["oblivion", "workspace", "error"],
                    [$"Requested page '{pageId}' was not found in '{loadResult.Workspace.ManifestPath}'."],
                    [])
            ];
        }

        return page.Cards
            .Select(card => card with
            {
                PageId = page.PresenterPageId,
                WorkspaceId = loadResult.Workspace.WorkspaceId,
            })
            .ToArray();
    }

    private static bool ShouldUseFallbackCatalog(PresenterProofOptions? proofOptions)
    {
        return string.IsNullOrWhiteSpace(proofOptions?.OblivionWorkspacePath) &&
            !OblivionWorkspacePaths.HasDefaultWorkspace();
    }

    private static IReadOnlyList<OblivionCard> CreateWorkspaceErrorCards(
        string pageId,
        OblivionWorkspaceLoadResult loadResult,
        PresenterProofOptions? proofOptions)
    {
        string workspacePath = OblivionWorkspacePaths.ResolveWorkspaceManifestPath(proofOptions?.OblivionWorkspacePath);
        string[] diagnosticLines = loadResult.Diagnostics.Count == 0
            ? [$"Workspace manifest '{workspacePath}' could not be loaded for page '{pageId}'."]
            : loadResult.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray();

        return
        [
            CreateStatusCard(
                $"oblivion-workspace-error-{SanitizeId(pageId)}",
                OblivionCardStatus.Failing,
                "Oblivion workspace load failed",
                "Presenter stayed live and rendered a bounded error card",
                ["oblivion", "workspace", "error"],
                diagnosticLines,
                [])
        ];
    }

    private static IReadOnlyList<OblivionCard> GetFallbackPageCards(string pageId)
    {
        return pageId switch
        {
            CardsPageId => CreateFallbackCardsPageCards(),
            ExecutionRoadmapPageId => CreateFallbackExecutionRoadmapCards(),
            ArtifactsPageId => CreateFallbackArtifactsPageCards(),
            DocsPageId => CreateFallbackDocsPageCards(),
            _ => throw new InvalidOperationException($"Unknown Oblivion page id '{pageId}'."),
        };
    }

    private static IReadOnlyList<OblivionCard> CreateFallbackCardsPageCards()
    {
        return
        [
            CreateStatusCard(
                "oblivion-fallback-note-card",
                OblivionCardStatus.Warning,
                "Oblivion fallback catalog active",
                "Sample workspace files were not available",
                ["oblivion", "fallback", "workspace"],
                [
                    "The presenter is using the hardcoded M11a fallback catalog.",
                    "Persisted workspace files were not found in the sample output.",
                    "Execution remains deferred.",
                ],
                []),
            CreateStatusCard(
                "oblivion-fallback-status-card",
                OblivionCardStatus.Deferred,
                "Persistence fallback state",
                "Safe fallback instead of a crash",
                ["json", "toml", "deferred"],
                [
                    "JSON/TOML persistence is expected in M11d.",
                    "When the sample workspace is unavailable, the fallback catalog keeps the shell renderable.",
                ],
                [])
        ];
    }

    private static IReadOnlyList<OblivionCard> CreateFallbackExecutionRoadmapCards()
    {
        return
        [
            CreateStatusCard(
                "oblivion-fallback-roadmap-card",
                OblivionCardStatus.Deferred,
                "Execution roadmap",
                "Fallback catalog",
                ["roslyn", "xunit", "deferred"],
                [
                    "Roslyn execution deferred.",
                    "xUnit [Fact] / [Theory] runtime deferred.",
                    "Persistence fallback is active because the sample workspace was not found.",
                ],
                [])
        ];
    }

    private static IReadOnlyList<OblivionCard> CreateFallbackArtifactsPageCards()
    {
        return
        [
            CreateStatusCard(
                "oblivion-fallback-artifacts-card",
                OblivionCardStatus.Placeholder,
                "Artifacts placeholder",
                "Fallback catalog",
                ["artifact", "fallback", "placeholder"],
                [
                    "Artifact metadata remains static in M11d.",
                    "Execution and generation remain deferred.",
                ],
                [])
        ];
    }

    private static IReadOnlyList<OblivionCard> CreateFallbackDocsPageCards()
    {
        return
        [
            CreateStatusCard(
                "oblivion-fallback-docs-card",
                OblivionCardStatus.Warning,
                "Docs dogfood unavailable",
                "Fallback catalog",
                ["docs", "markdown", "fallback"],
                [
                    "The persisted workspace was not found, so curated repo docs were not loaded.",
                    "Markdown remains the text-card body language only.",
                    "No editor, file watcher, Roslyn execution, xUnit execution, or Visionary implementation is added here.",
                ],
                [])
        ];
    }

    private static OblivionCard CreateStatusCard(
        string id,
        OblivionCardStatus status,
        string title,
        string? subtitle,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> bodyLines,
        IReadOnlyList<OblivionCardArtifact> artifacts)
    {
        return new OblivionCard(
            new OblivionCardId(id),
            OblivionCardKind.Status,
            status,
            title,
            subtitle,
            tags,
            OblivionMarkdownBody.CreatePlain(string.Join('\n', bodyLines)),
            [],
            artifacts);
    }

    private static string SanitizeId(string pageId)
    {
        return pageId.Replace('.', '-');
    }

    private static IReadOnlyList<UiRow> BuildInspectorRows(
        string pageId,
        OblivionInspectorSelection selection,
        StandardTheme theme,
        OblivionPageLayout layout)
    {
        List<UiRow> rows = [];
        double currentTop = 0;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-title",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 36,
            view: View.Text("Selected card inspector", color: theme.Colors.Foreground, size: TextSize.H1)));
        currentTop += 44;

        if (selection.SelectedCard is null)
        {
            rows.Add(Row.Anchor(
                id: $"{pageId}.inspector-empty",
                parent: "root",
                left: layout.InspectorLeft,
                top: currentTop,
                width: layout.InspectorWidth,
                height: 180,
                component: PresenterCard.BuildTextCard(
                    id: $"{pageId}.inspector-empty-card",
                    title: "No card selected",
                    badges: [],
                    lines:
                    [
                        "This page currently has no selected Oblivion card.",
                        "Click a compact card cell to restore selection.",
                        "M11g keeps the inspector static and non-executing.",
                    ],
                    theme: theme,
                    options: new PresenterCardOptions(layout.InspectorWidth, 180))));
            return rows;
        }

        OblivionBuiltCard card = selection.SelectedCard;

        foreach ((OblivionInspectorSectionView section, int index) in card.InspectorView.Sections.Select((section, index) => (section, index)))
        {
            rows.Add(Row.Anchor(
                id: $"{pageId}.inspector-section-{index}",
                parent: "root",
                left: layout.InspectorLeft,
                top: currentTop,
                width: layout.InspectorWidth,
                height: section.Height,
                component: BuildInspectorSection(section, pageId, theme, layout)));

            currentTop += section.Height + 24;
        }

        return rows;
    }

    private static IReadOnlyList<UiRow> BuildCompactCardListRows(
        string pageId,
        IReadOnlyList<OblivionBuiltCard> cards,
        OblivionInspectorSelection selection,
        StandardTheme theme,
        OblivionPageLayout layout)
    {
        List<UiRow> rows = [];
        double currentTop = 0;

        rows.Add(Row.Anchor(
            id: $"{pageId}.compact-title",
            parent: "root",
            left: 0,
            top: currentTop,
            width: layout.ContentWidth,
            height: 36,
            view: View.Text("Card list", color: theme.Colors.Foreground, size: TextSize.H1)));
        currentTop += 44;

        rows.Add(Row.Anchor(
            id: $"{pageId}.compact-subtitle",
            parent: "root",
            left: 0,
            top: currentTop,
            width: layout.ContentWidth,
            height: 20,
            view: View.Text(
                selection.SelectedCardId is null
                    ? "Scan, expand, read, and open inspector only when metadata or actions are needed."
                    : $"Selected card preserved: {selection.SelectedCardId}",
                color: theme.Colors.MutedForeground,
                size: TextSize.Sm)));
        currentTop += 36;

        if (selection.SelectedCardId is not null)
        {
            rows.Add(Row.Anchor(
                id: $"{pageId}.compact-inspector-open",
                parent: "root",
                left: 0,
                top: currentTop,
                width: 160,
                height: 36,
                component: StandardUI.Button(
                    "Open inspector",
                    id: $"{pageId}.compact-inspector-open.button",
                    action: PresenterNavigationActions.SetCompactPane(PresenterCompactPane.Inspector).ToAction(),
                    theme: theme,
                    variant: ButtonVariant.Outline,
                    size: ButtonSize.Medium)));
            currentTop += 52;
        }

        foreach (OblivionBuiltCard builtCard in cards)
        {
            double cardHeight = GetRenderedCardHeight(builtCard.CompactView, layout);
            bool isSelected = string.Equals(selection.SelectedCardId, builtCard.SourceCard.Id.Value, StringComparison.Ordinal);
            rows.Add(
                Row.Anchor(
                    builtCard.SourceCard.Id.Value + ".anchor",
                    "root",
                    left: 0,
                    top: currentTop,
                    width: layout.ContentWidth,
                    height: cardHeight,
                    component: OblivionCardRenderer.BuildCard(
                        builtCard.CompactView,
                        theme,
                        new OblivionCardRenderOptions(
                            Width: layout.ContentWidth,
                            Height: cardHeight),
                        isSelected)));

            currentTop += cardHeight + 24;
        }

        return rows;
    }

    private static IReadOnlyList<UiRow> BuildCompactInspectorRows(
        string pageId,
        OblivionInspectorSelection selection,
        StandardTheme theme,
        OblivionPageLayout layout)
    {
        List<UiRow> rows =
        [
            Row.Anchor(
                id: $"{pageId}.compact-back",
                parent: "root",
                left: 0,
                top: 0,
                width: 120,
                height: 36,
                component: StandardUI.Button(
                    "Back",
                    id: $"{pageId}.compact-back.button",
                    action: PresenterNavigationActions.SetCompactPane(PresenterCompactPane.CardList).ToAction(),
                    theme: theme,
                    variant: ButtonVariant.Outline,
                    size: ButtonSize.Medium)),
            Row.Anchor(
                id: $"{pageId}.compact-inspector-title",
                parent: "root",
                left: 0,
                top: 52,
                width: layout.ContentWidth,
                height: 36,
                view: View.Text("Inspector", color: theme.Colors.Foreground, size: TextSize.H1)),
        ];

        if (selection.SelectedCard is null)
        {
            rows.Add(
                Row.Anchor(
                    id: $"{pageId}.compact-inspector-empty",
                    parent: "root",
                    left: 0,
                    top: 104,
                    width: layout.ContentWidth,
                    height: 180,
                    component: PresenterCard.BuildTextCard(
                        id: $"{pageId}.compact-inspector-empty-card",
                        title: "No card selected",
                        badges: [],
                        lines:
                        [
                            "No selected card is available for compact inspector view.",
                            "Use Back to return to the card list.",
                            "This remains a deterministic shell swap rather than a fluid resize path.",
                        ],
                        theme: theme,
                        options: new PresenterCardOptions(layout.ContentWidth, 180))));
            return rows;
        }

        double currentTop = 104;
        foreach ((OblivionInspectorSectionView section, int index) in selection.SelectedCard.InspectorView.Sections.Select((section, index) => (section, index)))
        {
            rows.Add(Row.Anchor(
                id: $"{pageId}.compact-inspector-section-{index}",
                parent: "root",
                left: 0,
                top: currentTop,
                width: layout.ContentWidth,
                height: section.Height,
                component: BuildInspectorSection(section, pageId, theme, layout)));

            currentTop += section.Height + 24;
        }

        return rows;
    }

    private static UiNode BuildInspectorSection(
        OblivionInspectorSectionView section,
        string pageId,
        StandardTheme theme,
        OblivionPageLayout layout)
    {
        string sectionId = section.Id[(section.Id.LastIndexOf('.') + 1)..];
        string cardId = sectionId.Replace(".", "-", StringComparison.Ordinal);

        if (section.Body is OblivionInspectorRawMarkdownSourceBodyContent rawMarkdownBody)
        {
            OblivionMarkdownRenderer.OblivionScrollableCodeSurfaceRenderResult rawSource = OblivionMarkdownRenderer.BuildInspectorRawSourceBody(
                $"{pageId}.{cardId}.markdown-source",
                rawMarkdownBody.Body,
                OblivionCardRenderer.MarkdownReadingStyle,
                layout.InspectorWidth - 34,
                section.Height - 84);

            return PresenterCard.BuildHostedCard(
                id: $"{pageId}.{cardId}.card",
                title: section.Title,
                badges: section.Badges,
                body: rawSource.Node,
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, section.Height, ClipContent: section.ClipContent));
        }

        IReadOnlyList<string> lines = section.Body is OblivionInspectorTextBodyContent textBody
            ? textBody.Lines
            : ["Unsupported inspector body."];

        return PresenterCard.BuildTextCard(
            id: $"{pageId}.{cardId}.card",
            title: section.Title,
            badges: section.Badges,
            lines: lines,
            theme: theme,
            options: new PresenterCardOptions(layout.InspectorWidth, section.Height, ClipContent: section.ClipContent));
    }

    private static OblivionInspectorSelection ResolveSelection(
        string pageId,
        IReadOnlyList<OblivionBuiltCard> cards,
        PresenterNavigationState? navigationState)
    {
        string? selectedCardId = navigationState is null
            ? (cards.Count == 0 ? null : cards[0].SourceCard.Id.Value)
            : navigationState.GetSelectedCardId(pageId, cards.Select(card => card.SourceCard).ToArray());
        OblivionBuiltCard? selectedBuiltCard = selectedCardId is null
            ? null
            : cards.FirstOrDefault(card => string.Equals(card.SourceCard.Id.Value, selectedCardId, StringComparison.Ordinal));
        return new OblivionInspectorSelection(cards, selectedBuiltCard, selectedCardId);
    }

    private static OblivionCardLocalState ResolveLocalState(
        string pageId,
        OblivionCard card,
        PresenterNavigationState? navigationState)
    {
        OblivionCardViewState viewState = navigationState?.GetCardViewState(pageId, card.Id.Value)
            ?? OblivionCardViewState.Collapsed;

        return OblivionCardLocalState.CreateDefault(card.Id) with
        {
            IsExpanded = viewState.IsExpanded,
            BodyScrollOffset = viewState.BodyScrollOffset,
        };
    }

    private static double GetRenderedCardHeight(OblivionCompactCardView view, OblivionPageLayout layout)
    {
        if (view.IsExpanded && view.Body is OblivionCompactMarkdownBodyContent)
        {
            return Math.Max(view.ExpandedPreferredHeight, layout.ExpandedMarkdownCardHeight);
        }

        return view.IsExpanded
            ? view.ExpandedPreferredHeight
            : view.PreferredHeight;
    }

    private static double GetCardsColumnHeight(IReadOnlyList<OblivionBuiltCard> cards, OblivionPageLayout layout)
    {
        if (cards.Count == 0)
        {
            return 0;
        }

        double height = 0;
        for (int index = 0; index < cards.Count; index++)
        {
            height += GetRenderedCardHeight(cards[index].CompactView, layout);
            if (index < cards.Count - 1)
            {
                height += 24;
            }
        }

        return height;
    }

    private static double GetInspectorHeight(OblivionInspectorSelection selection)
    {
        return selection.SelectedCard is null ? 240 : selection.SelectedCard.InspectorView.PreferredHeight;
    }

    private static string[] GetPhaseCloseoutDeferredWork()
    {
        return
        [
            "Markdown renderer",
            "Markdown editor",
            "Roslyn compilation and execution",
            "xUnit [Fact] and [Theory] runtime",
            "Effect execution host",
            "Artifact generation and execution",
            "Visionary code editor/source workspace",
        ];
    }

    public static (string jsonPath, string textPath) WriteEffectRoutingManifest(
        string outputDirectory,
        PresenterNavigationState navigationState,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(navigationState);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-card-effect-routing-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-card-effect-routing-manifest.txt");

        IReadOnlyList<OblivionCard> cards = CreateAllCards(proofOptions)
            .OrderBy(card => card.Id.Value, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<OblivionBuiltCard> builtCards = cards
            .Select(card => CardHandlers.BuildCard(card, card.PageId, card.WorkspaceId, navigationState.EffectState))
            .OrderBy(card => card.SourceCard.Id.Value, StringComparer.Ordinal)
            .ToArray();

        string[] supportedEffectKinds = Enum.GetValues<OblivionCardEffectKind>()
            .Select(kind => kind.ToString())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] deferredEffectKinds = supportedEffectKinds
            .Where(kind => !string.Equals(kind, nameof(OblivionCardEffectKind.Custom), StringComparison.Ordinal))
            .ToArray();
        string[] handlersWithActions = builtCards
            .Where(card => card.RuntimeModel.Actions.Count > 0)
            .GroupBy(card => card.SourceCard.Kind)
            .Select(group =>
            {
                string kind = OblivionWorkspaceValidator.GetCardKindValue(group.Key);
                string actions = string.Join(
                    ",",
                    group.SelectMany(card => card.RuntimeModel.Actions)
                        .Select(action => action.Id)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal));
                return $"{kind}:{actions}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] deferredWork =
        [
            "Dominatus-backed effect execution host",
            "Roslyn compilation and execution",
            "xUnit [Fact] and [Theory] runtime",
            "Real artifact opening/export",
            "UI action hit regions for inspector action controls",
            "Visionary code editor/source workspace",
        ];

        var manifest = new
        {
            milestone = "M12f",
            kind = "oblivion-card-effect-routing",
            actionRoutingEnabled = true,
            effectRouterEnabled = true,
            effectsExecutable = false,
            actionsExecutable = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            supportedEffectKinds,
            deferredEffectKinds,
            handlersWithActions,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M12f",
            "kind=oblivion-card-effect-routing",
            "actionRoutingEnabled=true",
            "effectRouterEnabled=true",
            "effectsExecutable=false",
            "actionsExecutable=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"supportedEffectKinds={string.Join(",", supportedEffectKinds)}",
            $"deferredEffectKinds={string.Join(",", deferredEffectKinds)}",
            $"handlersWithActions={string.Join(",", handlersWithActions)}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteMarkdownRenderingManifest(
        string outputDirectory,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-markdown-rendering-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-markdown-rendering-manifest.txt");

        IReadOnlyList<OblivionCard> cards = CreateAllCards(proofOptions)
            .OrderBy(card => card.Id.Value, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<OblivionCard> markdownCards = cards
            .Where(card => card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown)
            .OrderBy(card => card.Id.Value, StringComparer.Ordinal)
            .ToArray();

        string[] deferredWork =
        [
            "Markdown editor",
            "File watcher / live editing",
            "Roslyn compilation and execution",
            "xUnit [Fact] and [Theory] runtime",
            "Image/media/table/video typed card implementation",
            "Single-file Markdown export/import pipeline",
            "Visionary code editor/source workspace",
        ];

        int diagnosticsCount = markdownCards.Sum(card => card.Body.Diagnostics.Count);
        string[] markdownCardIds = markdownCards.Select(card => card.Id.Value).ToArray();
        string[] markdownBodySourcePaths = markdownCards
            .Select(card => card.Body.BodySourcePath ?? "<inline>")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] supportedRenderedBlocks =
        [
            "heading",
            "paragraph",
            "bullet-list",
            "ordered-list",
            "fenced-code-block",
            "thematic-break",
        ];
        string[] supportedRenderedInlines =
        [
            "text",
            "inline-code",
            "strong",
            "emphasis",
            "link-label-and-target",
        ];

        var manifest = new
        {
            milestone = "M12c",
            kind = "oblivion-markdown-rendering",
            markdownFrontend = "Copeland.Markdown",
            documentMirRendered = true,
            compactPreviewRendered = true,
            inspectorRendered = true,
            diagnosticsRendered = true,
            editorImplemented = false,
            fileWatcherImplemented = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            supportedRenderedBlocks,
            supportedRenderedInlines,
            markdownCardsLoaded = markdownCardIds,
            markdownBodyFilesLoaded = markdownBodySourcePaths,
            markdownDiagnosticsCount = diagnosticsCount,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M12c",
            "kind=oblivion-markdown-rendering",
            "markdownFrontend=Copeland.Markdown",
            "documentMirRendered=true",
            "compactPreviewRendered=true",
            "inspectorRendered=true",
            "diagnosticsRendered=true",
            "editorImplemented=false",
            "fileWatcherImplemented=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"supportedRenderedBlocks={string.Join(",", supportedRenderedBlocks)}",
            $"supportedRenderedInlines={string.Join(",", supportedRenderedInlines)}",
            $"markdownCardsLoaded={string.Join(",", markdownCardIds)}",
            $"markdownBodyFilesLoaded={string.Join(",", markdownBodySourcePaths)}",
            $"markdownDiagnosticsCount={diagnosticsCount}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    public static (string jsonPath, string textPath) WriteDocsDogfoodManifest(
        string outputDirectory,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        string workspacePath = OblivionWorkspacePaths.ResolveWorkspaceManifestPath(proofOptions?.OblivionWorkspacePath);
        return OblivionDocsDogfoodCatalog.WriteManifest(outputDirectory, workspacePath);
    }

    public static (string jsonPath, string textPath) WriteAgenticCardContractManifest(
        string outputDirectory,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-agentic-card-contract-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-agentic-card-contract-manifest.txt");

        IReadOnlyList<OblivionCard> cards = CreateAllCards(proofOptions)
            .OrderBy(card => card.Id.Value, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<OblivionBuiltCard> builtCards = cards
            .Select(card => CardHandlers.BuildCard(card, card.PageId, card.WorkspaceId, OblivionCardEffectState.Empty))
            .OrderBy(card => card.SourceCard.Id.Value, StringComparer.Ordinal)
            .ToArray();
        string[] handlerKinds = CardHandlers.RegisteredKinds
            .Select(OblivionWorkspaceValidator.GetCardKindValue)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] deferredWork =
        [
            "Roslyn compilation and execution",
            "xUnit [Fact] and [Theory] runtime",
            "Runtime action dispatch",
            "Dominatus effect execution",
            "Artifact generation runtime",
            "Visionary code editor/source workspace",
            "Markdown editor",
            "File watcher / live editing",
        ];

        var manifest = new
        {
            milestone = "M12e",
            kind = "oblivion-agentic-card-contract",
            cardAsAppletDoctrine = true,
            localityOfChangeDoctrine = true,
            handlersRegistered = handlerKinds.Length,
            handlerKinds,
            actionsExecutable = false,
            effectsExecutable = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            diagnosticsAggregated = builtCards.Sum(card => card.RuntimeModel.Diagnostics.Count),
            artifactsDeclared = builtCards.Sum(card => card.RuntimeModel.Artifacts.Count),
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M12e",
            "kind=oblivion-agentic-card-contract",
            "cardAsAppletDoctrine=true",
            "localityOfChangeDoctrine=true",
            $"handlersRegistered={handlerKinds.Length}",
            $"handlerKinds={string.Join(",", handlerKinds)}",
            "actionsExecutable=false",
            "effectsExecutable=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"diagnosticsAggregated={builtCards.Sum(card => card.RuntimeModel.Diagnostics.Count)}",
            $"artifactsDeclared={builtCards.Sum(card => card.RuntimeModel.Artifacts.Count)}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

}

public sealed record OblivionPageLayout(
    int ContentWidth,
    int CardsColumnWidth,
    int InspectorLeft,
    int InspectorWidth,
    int ViewportHeight,
    int ExpandedMarkdownCardHeight)
{
    public static OblivionPageLayout CreateWide(int contentWidth, int viewportHeight)
    {
        const int columnGap = 24;
        int inspectorWidth = Math.Max(284, Math.Min(332, (int)Math.Floor(contentWidth * 0.4)));
        int cardsColumnWidth = Math.Max(320, contentWidth - inspectorWidth - columnGap);
        int inspectorLeft = cardsColumnWidth + columnGap;
        int expandedMarkdownCardHeight = Math.Max(480, viewportHeight - 20);
        return new OblivionPageLayout(contentWidth, cardsColumnWidth, inspectorLeft, inspectorWidth, viewportHeight, expandedMarkdownCardHeight);
    }

    public static OblivionPageLayout CreateCompact(int contentWidth, int viewportHeight)
    {
        int expandedMarkdownCardHeight = Math.Max(360, viewportHeight - 16);
        return new OblivionPageLayout(contentWidth, contentWidth, 0, contentWidth, viewportHeight, expandedMarkdownCardHeight);
    }
}
