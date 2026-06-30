using System.Text.Json;
using Machina.Core.Authoring;
using Machina.Core.Flat;
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
        PresenterProofOptions? proofOptions = null,
        PresenterNavigationState? navigationState = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
        OblivionInspectorSelection selection = ResolveSelection(pageId, cards, navigationState);
        OblivionPageLayout layout = OblivionPageLayout.Create(contentWidth);
        List<UiRow> rows = [];
        double currentTop = 0;

        rows.Add(
            Row.Anchor(
                id: $"{pageId}.cards-panel",
                parent: "root",
                left: 0,
                top: 0,
                width: layout.CardsColumnWidth,
                height: Math.Max(160, GetCardsColumnHeight(cards)),
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

        foreach (OblivionCard card in cards)
        {
            double cardHeight = GetCardHeight(card);
            bool isSelected = string.Equals(selection.SelectedCardId, card.Id.Value, StringComparison.Ordinal);
            rows.Add(
                Row.Anchor(
                    card.Id.Value + ".anchor",
                    "root",
                    left: 0,
                    top: currentTop,
                    width: layout.CardsColumnWidth,
                    height: cardHeight,
                    component: OblivionCardRenderer.BuildCard(
                        card,
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

    public static double GetPageContentHeight(string pageId, PresenterProofOptions? proofOptions = null)
    {
        IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
        return Math.Max(GetCardsColumnHeight(cards), 1440);
    }

    public static double GetCardHeight(OblivionCard card)
    {
        return card.Kind switch
        {
            OblivionCardKind.CodeFact => 248,
            OblivionCardKind.CodeTheory => 312,
            OblivionCardKind.UiPreview => 184,
            OblivionCardKind.Artifact when card.Artifacts.Count > 1 => 196,
            _ => 168,
        };
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

        string[] deferredWork = GetPhaseCloseoutDeferredWork();

        var manifest = new
        {
            milestone = "M11f",
            kind = "oblivion-card-selection-inspector",
            selectionModel = "page-local-selected-card-id-by-page-id",
            inspectorEnabled = true,
            defaultSelectionPolicy = "first-card-when-no-explicit-selection; empty-when-cleared",
            selectedCardsExported = exportedSelections,
            actionsExecutable = false,
            artifactsExecutable = false,
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
            "milestone=M11f",
            "kind=oblivion-card-selection-inspector",
            "selectionModel=page-local-selected-card-id-by-page-id",
            "inspectorEnabled=true",
            "defaultSelectionPolicy=first-card-when-no-explicit-selection; empty-when-cleared",
            $"selectedCardsExported={string.Join(",", exportedSelections)}",
            "actionsExecutable=false",
            "artifactsExecutable=false",
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
        ResolvedLayoutDocument resolved)
    {
        IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
        List<OblivionCardHitTarget> targets = [];

        foreach (OblivionCard card in cards)
        {
            PresenterCardFrame frame = OblivionCardRenderer.DescribeFrame(resolved, card.Id.Value);
            targets.Add(new OblivionCardHitTarget(pageId, card.Id.Value, frame.Bounds));
        }

        return new OblivionPageInteractionMap(pageId, targets);
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

        OblivionCard card = selection.SelectedCard;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-summary",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 188,
            component: PresenterCard.BuildTextCard(
                id: $"{pageId}.inspector-summary-card",
                title: card.Title,
                badges: [],
                lines:
                [
                    $"Kind: {KindLabel(card.Kind)}",
                    $"Status: {StatusLabel(card.Status)}",
                    $"Body format: {BodyFormatLabel(card.Body.Format)}",
                    card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                        ? "Selected card details are expanded here while the page model stays a stack of typed cards and Markdown remains only the text-card body language."
                        : "Selected card details are expanded here so the main cards can stay compact while the typed-card page model stays visible.",
                ],
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, 188))));
        currentTop += 212;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-metadata",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 236,
            component: PresenterCard.BuildTextCard(
                id: $"{pageId}.inspector-metadata-card",
                title: "Metadata",
                badges: [],
                lines:
                [
                    $"Card ID: {card.Id.Value}",
                    $"Page ID: {pageId}",
                    $"Source path: {card.SourcePath ?? "<none>"}",
                    $"Body source path: {card.Body.BodySourcePath ?? "<inline>"}",
                    $"Workspace: {card.WorkspaceId ?? "<none>"}",
                    $"Tags: {FormatTags(card.Tags)}",
                ],
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, 236))));
        currentTop += 260;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-body",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 448,
            component: PresenterCard.BuildHostedCard(
                id: $"{pageId}.inspector-body-card",
                title: "Body",
                badges: card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? ["DocumentMir rendered", "Static Markdown"]
                    : [],
                body: card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? OblivionMarkdownRenderer.BuildInspectorBody(
                        $"{pageId}.inspector-markdown",
                        card.Body,
                        theme,
                        layout.InspectorWidth - 34)
                    : StandardUI.TextBlock(
                        Machina.Standard.Text.Text.Plain(
                            string.Join(Environment.NewLine, BuildInspectorLines(card.Body))),
                        id: $"{pageId}.inspector-plain",
                        theme: theme),
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, 448, ClipContent: false))));
        currentTop += 472;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-diagnostics",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 236,
            component: PresenterCard.BuildTextCard(
                id: $"{pageId}.inspector-diagnostics-card",
                title: "Markdown diagnostics",
                badges: [],
                lines: card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? BuildDiagnosticLines(card.Body.Diagnostics)
                    : ["Not a Markdown body."],
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, 236))));
        currentTop += 260;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-actions",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 212,
            component: PresenterCard.BuildTextCard(
                id: $"{pageId}.inspector-actions-card",
                title: "Actions metadata",
                badges: [],
                lines: card.Actions.Count == 0
                    ? ["No actions declared on this card.", "Actions remain metadata only and are not executable."]
                    : ["Actions remain metadata only and are not executable.", .. card.Actions.Select(action => $"{action.Id} | {action.Label} | {(action.Enabled ? "enabled metadata" : "disabled metadata")}")],
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, 212))));
        currentTop += 236;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-artifacts",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 236,
            component: PresenterCard.BuildTextCard(
                id: $"{pageId}.inspector-artifacts-card",
                title: "Artifacts metadata",
                badges: [],
                lines: card.Artifacts.Count == 0
                    ? ["No artifacts declared on this card."]
                    : card.Artifacts.Select(artifact => $"{artifact.Id} | {artifact.Label} | {artifact.Kind} | path {artifact.Path ?? "<none>"} | generated {artifact.Generated.ToString().ToLowerInvariant()}").ToArray(),
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, 236))));
        currentTop += 260;

        rows.Add(Row.Anchor(
            id: $"{pageId}.inspector-execution",
            parent: "root",
            left: layout.InspectorLeft,
            top: currentTop,
            width: layout.InspectorWidth,
            height: 212,
            component: PresenterCard.BuildTextCard(
                id: $"{pageId}.inspector-execution-card",
                title: "Execution result",
                badges: [],
                lines:
                [
                    "Not executed in M11g.",
                    "Markdown cards come first; Roslyn/xUnit execution deferred to M13+.",
                    card.Kind is OblivionCardKind.CodeFact or OblivionCardKind.CodeTheory
                        ? "CodeFact/CodeTheory source is static placeholder content only and remains deferred."
                        : "Actions and artifacts are displayed as metadata only.",
                ],
                theme: theme,
                options: new PresenterCardOptions(layout.InspectorWidth, 212))));

        return rows;
    }

    private static OblivionInspectorSelection ResolveSelection(
        string pageId,
        IReadOnlyList<OblivionCard> cards,
        PresenterNavigationState? navigationState)
    {
        string? selectedCardId = navigationState is null
            ? (cards.Count == 0 ? null : cards[0].Id.Value)
            : navigationState.GetSelectedCardId(pageId, cards);
        OblivionCard? selectedCard = selectedCardId is null
            ? null
            : cards.FirstOrDefault(card => string.Equals(card.Id.Value, selectedCardId, StringComparison.Ordinal));
        return new OblivionInspectorSelection(cards, selectedCard, selectedCardId);
    }

    private static double GetCardsColumnHeight(IReadOnlyList<OblivionCard> cards)
    {
        if (cards.Count == 0)
        {
            return 0;
        }

        double height = 0;
        for (int index = 0; index < cards.Count; index++)
        {
            height += GetCardHeight(cards[index]);
            if (index < cards.Count - 1)
            {
                height += 24;
            }
        }

        return height;
    }

    private static double GetInspectorHeight(OblivionInspectorSelection selection)
    {
        return selection.SelectedCard is null ? 240 : 1760;
    }

    private static string FormatTags(IReadOnlyList<string> tags)
    {
        return tags.Count == 0
            ? "<none>"
            : string.Join(", ", tags);
    }

    private static string KindLabel(OblivionCardKind kind)
    {
        return kind switch
        {
            OblivionCardKind.Note => "Note",
            OblivionCardKind.Status => "Status",
            OblivionCardKind.UiPreview => "UI Preview",
            OblivionCardKind.Artifact => "Artifact",
            OblivionCardKind.CodeFact => "Code Fact",
            OblivionCardKind.CodeTheory => "Code Theory",
            _ => kind.ToString(),
        };
    }

    private static string StatusLabel(OblivionCardStatus status)
    {
        return status switch
        {
            OblivionCardStatus.Idle => "Idle",
            OblivionCardStatus.Passing => "Passing",
            OblivionCardStatus.Failing => "Failing",
            OblivionCardStatus.Warning => "Warning",
            OblivionCardStatus.Deferred => "Deferred",
            OblivionCardStatus.Placeholder => "Placeholder",
            _ => status.ToString(),
        };
    }

    private static string[] GetPhaseCloseoutDeferredWork()
    {
        return
        [
            "Markdown renderer",
            "Markdown editor",
            "Roslyn compilation and execution",
            "xUnit [Fact] and [Theory] runtime",
            "Action execution",
            "Artifact generation and execution",
            "Visionary code editor/source workspace",
        ];
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

    private static string BodyFormatLabel(OblivionCardBodyFormat format)
    {
        return format switch
        {
            OblivionCardBodyFormat.Plain => "Plain",
            OblivionCardBodyFormat.CopelandMarkdown => "Copeland Markdown",
            _ => format.ToString(),
        };
    }
}

public sealed record OblivionPageLayout(
    int ContentWidth,
    int CardsColumnWidth,
    int InspectorLeft,
    int InspectorWidth)
{
    public static OblivionPageLayout Create(int contentWidth)
    {
        const int columnGap = 24;
        int inspectorWidth = Math.Max(284, Math.Min(332, (int)Math.Floor(contentWidth * 0.4)));
        int cardsColumnWidth = Math.Max(320, contentWidth - inspectorWidth - columnGap);
        int inspectorLeft = cardsColumnWidth + columnGap;
        return new OblivionPageLayout(contentWidth, cardsColumnWidth, inspectorLeft, inspectorWidth);
    }
}
