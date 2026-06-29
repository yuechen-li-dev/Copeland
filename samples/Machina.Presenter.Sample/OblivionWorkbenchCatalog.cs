using System.Text.Json;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class OblivionWorkbenchCatalog
{
    public const string CardsPageId = "oblivion.cards";
    public const string ExecutionRoadmapPageId = "oblivion.execution-roadmap";
    public const string ArtifactsPageId = "oblivion.artifacts";

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

    public static IReadOnlyList<OblivionCard> CreateAllCards(PresenterProofOptions? proofOptions = null)
    {
        return
        [
            .. GetPageCards(CardsPageId, proofOptions),
            .. GetPageCards(ExecutionRoadmapPageId, proofOptions),
            .. GetPageCards(ArtifactsPageId, proofOptions),
        ];
    }

    public static IReadOnlyList<UiRow> BuildPageRows(
        string pageId,
        StandardTheme theme,
        int contentWidth,
        PresenterProofOptions? proofOptions = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
        List<UiRow> rows = [];
        double currentTop = 0;

        foreach (OblivionCard card in cards)
        {
            double cardHeight = GetCardHeight(card);
            rows.Add(
                Row.Anchor(
                    card.Id.Value + ".anchor",
                    "root",
                    left: 0,
                    top: currentTop,
                    width: contentWidth,
                    height: cardHeight,
                    component: OblivionCardRenderer.BuildCard(
                        card,
                        theme,
                        new OblivionCardRenderOptions(
                            Width: contentWidth,
                            Height: cardHeight))));

            currentTop += cardHeight + 24;
        }

        return rows;
    }

    public static double GetPageContentHeight(string pageId, PresenterProofOptions? proofOptions = null)
    {
        IReadOnlyList<OblivionCard> cards = GetPageCards(pageId, proofOptions);
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
            ? 3
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

        return page.Cards;
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
            bodyLines,
            [],
            artifacts);
    }

    private static string SanitizeId(string pageId)
    {
        return pageId.Replace('.', '-');
    }
}
