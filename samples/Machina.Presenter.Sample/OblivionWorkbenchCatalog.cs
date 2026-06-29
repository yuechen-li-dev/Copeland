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

    public const double CardsPageContentHeight = 1496;
    public const double ExecutionRoadmapPageContentHeight = 492;
    public const double ArtifactsPageContentHeight = 432;

    public static IReadOnlyList<OblivionCard> CreateCardsPageCards()
    {
        return
        [
            new OblivionCard(
                new OblivionCardId("oblivion-intro-note-card"),
                OblivionCardKind.Note,
                OblivionCardStatus.Idle,
                "Oblivion workbench substrate",
                "Notebook/card/workbench layer",
                ["oblivion", "m11a", "static proof"],
                [
                    "Oblivion is the notebook/card layer for Machina Workbench.",
                    "Cards are bounded cells with deterministic layout.",
                    "Execution is deferred to a later milestone.",
                ],
                [],
                []),
            new OblivionCard(
                new OblivionCardId("oblivion-static-status-card"),
                OblivionCardKind.Status,
                OblivionCardStatus.Passing,
                "Current substrate status",
                "Static proof state",
                ["direct-outline-static", "msdf", "presenter", "deferred"],
                [
                    "DirectOutlineStatic: ready",
                    "MSDF: experimental",
                    "Presenter shell: active",
                    "Roslyn execution: deferred",
                ],
                [],
                []),
            new OblivionCard(
                new OblivionCardId("oblivion-ui-preview-card"),
                OblivionCardKind.UiPreview,
                OblivionCardStatus.Placeholder,
                "UI preview placeholder",
                "Future visual proof surface",
                ["preview", "ui", "placeholder"],
                [
                    "Future card will render a Machina UI document preview here.",
                    "This static proof keeps the card bounded without introducing live rendering behavior.",
                ],
                [new OblivionCardAction("open-preview", "Open preview", false)],
                []),
            new OblivionCard(
                new OblivionCardId("oblivion-artifact-placeholder-card"),
                OblivionCardKind.Artifact,
                OblivionCardStatus.Placeholder,
                "Artifact placeholder",
                "Export surface placeholder",
                ["artifact", "export", "placeholder"],
                [
                    "Future card will attach/export PNG, JSON, TOML, or source artifacts.",
                    "M11a only proves the static card substrate and metadata surface.",
                ],
                [new OblivionCardAction("open-artifacts", "Open artifacts", false)],
                [
                    new OblivionCardArtifact("artifact-png", "PNG proof", "png", null),
                    new OblivionCardArtifact("artifact-json", "JSON manifest", "json", null),
                ]),
            new OblivionCard(
                new OblivionCardId("oblivion-code-fact-card"),
                OblivionCardKind.CodeFact,
                OblivionCardStatus.Deferred,
                "Code fact placeholder",
                "not executed in M11a",
                ["code", "fact", "deferred"],
                [
                    "[Fact]",
                    "public void SettingsCard_Renders()",
                    "{",
                    "    // Execution deferred.",
                    "}",
                    "not executed in M11a",
                ],
                [new OblivionCardAction("run-fact", "Run fact", false)],
                [new OblivionCardArtifact("fact-source", "Source snippet", "code", null)]),
            new OblivionCard(
                new OblivionCardId("oblivion-code-theory-card"),
                OblivionCardKind.CodeTheory,
                OblivionCardStatus.Deferred,
                "Code theory placeholder",
                "not executed in M11a",
                ["code", "theory", "deferred"],
                [
                    "[Theory]",
                    "public void TextProof_RendersAtSize(int size)",
                    "[InlineData(16)]",
                    "[InlineData(24)]",
                    "{",
                    "    // Execution deferred.",
                    "}",
                    "not executed in M11a",
                ],
                [new OblivionCardAction("run-theory", "Run theory", false)],
                [new OblivionCardArtifact("theory-source", "Source snippet", "code", null)]),
        ];
    }

    public static IReadOnlyList<OblivionCard> CreateExecutionRoadmapCards()
    {
        return
        [
            new OblivionCard(
                new OblivionCardId("oblivion-execution-roadmap-card"),
                OblivionCardKind.Status,
                OblivionCardStatus.Deferred,
                "Execution roadmap",
                "Card model proven first",
                ["roslyn", "xunit", "deferred"],
                [
                    "Roslyn execution deferred.",
                    "xUnit [Fact] / [Theory] deferred.",
                    "Artifact capture deferred.",
                    "Card model proven first.",
                ],
                [],
                []),
            new OblivionCard(
                new OblivionCardId("oblivion-visionary-note-card"),
                OblivionCardKind.Note,
                OblivionCardStatus.Deferred,
                "Visionary relationship",
                "Future source workspace layer",
                ["visionary", "future", "docs-only"],
                [
                    "Visionary is the future code editor/source workspace layer.",
                    "M11a does not implement Visionary.",
                ],
                [],
                []),
        ];
    }

    public static IReadOnlyList<OblivionCard> CreateArtifactsPageCards()
    {
        return
        [
            new OblivionCard(
                new OblivionCardId("oblivion-artifacts-page-card"),
                OblivionCardKind.Artifact,
                OblivionCardStatus.Placeholder,
                "Artifact lane placeholder",
                "Static export-facing proof",
                ["artifacts", "png", "json", "toml"],
                [
                    "Artifacts tab keeps export-facing placeholders visible inside the workbench shell.",
                    "M11a exports presenter PNGs and card model manifests without introducing execution.",
                ],
                [new OblivionCardAction("capture-artifact", "Capture artifact", false)],
                [
                    new OblivionCardArtifact("png-proof", "Presenter PNG", "png", "artifacts/m11a/presenter-oblivion-cards.png"),
                    new OblivionCardArtifact("manifest-json", "Card model manifest", "json", "artifacts/m11a/oblivion-card-model-manifest.json"),
                    new OblivionCardArtifact("manifest-text", "Card model manifest text", "txt", "artifacts/m11a/oblivion-card-model-manifest.txt"),
                ]),
            new OblivionCard(
                new OblivionCardId("oblivion-artifacts-export-policy-card"),
                OblivionCardKind.Note,
                OblivionCardStatus.Idle,
                "Artifact policy",
                "Deterministic local proof outputs",
                ["deterministic", "local proof"],
                [
                    "Exports stay under artifacts/m11a for this milestone.",
                    "No timestamp is included in the Oblivion card model manifest by default.",
                ],
                [],
                []),
        ];
    }

    public static IReadOnlyList<OblivionCard> CreateAllCards()
    {
        return
        [
            .. CreateCardsPageCards(),
            .. CreateExecutionRoadmapCards(),
            .. CreateArtifactsPageCards(),
        ];
    }

    public static IReadOnlyList<UiRow> BuildPageRows(
        string pageId,
        StandardTheme theme,
        int contentWidth)
    {
        IReadOnlyList<OblivionCard> cards = pageId switch
        {
            CardsPageId => CreateCardsPageCards(),
            ExecutionRoadmapPageId => CreateExecutionRoadmapCards(),
            ArtifactsPageId => CreateArtifactsPageCards(),
            _ => throw new InvalidOperationException($"Unknown Oblivion page id '{pageId}'."),
        };

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

    public static (string jsonPath, string textPath) WriteManifest(string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        string jsonPath = Path.Combine(outputDirectory, "oblivion-card-model-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-card-model-manifest.txt");

        string[] expectedArtifactNames =
        [
            "presenter-oblivion-cards.png",
            "presenter-oblivion-execution-roadmap.png",
            "presenter-oblivion-artifacts.png",
            "presenter-oblivion-scrolled.png",
            "oblivion-card-model-manifest.json",
            "oblivion-card-model-manifest.txt",
        ];

        string[] artifactsGenerated = expectedArtifactNames
            .Where(fileName => File.Exists(Path.Combine(outputDirectory, fileName)) || fileName.StartsWith("oblivion-card-model-manifest", StringComparison.Ordinal))
            .ToArray();

        string[] deferredWork =
        [
            "Roslyn execution",
            "xUnit [Fact] and [Theory] runtime",
            "Artifact capture from card execution",
            "Visionary code editor/source workspace implementation",
            "Markdown editing",
        ];

        OblivionCard[] cards = CreateAllCards().OrderBy(card => card.Id.Value, StringComparer.Ordinal).ToArray();

        var manifest = new
        {
            milestone = "M11a",
            kind = "oblivion-card-model-proof",
            cardKinds = Enum.GetNames<OblivionCardKind>(),
            cardStatuses = Enum.GetNames<OblivionCardStatus>(),
            cardsRendered = cards.Select(card => new
            {
                id = card.Id.Value,
                kind = card.Kind.ToString(),
                status = card.Status.ToString(),
                title = card.Title,
            }).ToArray(),
            executionEnabled = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            artifactsGenerated,
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
            "milestone=M11a",
            "kind=oblivion-card-model-proof",
            $"cardKinds={string.Join(",", Enum.GetNames<OblivionCardKind>())}",
            $"cardStatuses={string.Join(",", Enum.GetNames<OblivionCardStatus>())}",
            "executionEnabled=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            $"artifactsGenerated={string.Join(",", artifactsGenerated)}",
            $"deferredWork={string.Join(" | ", deferredWork)}",
            "cardsRendered:",
            .. cards.Select(card => $"  {card.Id.Value}:{card.Kind}:{card.Status}:{card.Title}"),
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }
}
