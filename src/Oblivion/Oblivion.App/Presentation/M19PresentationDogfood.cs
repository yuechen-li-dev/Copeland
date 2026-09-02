using Oblivion.Presentation;
using Oblivion.Product;
using static Oblivion.Presentation.Content;
using static Oblivion.Presentation.Layout;

namespace Oblivion.App;

public static class M19PresentationDogfood
{
    public const string Id = "m19-architecture";

    public static MaterializedPresentation Materialize(string? repositoryRoot = null)
    {
        return PresentationMaterializer.Materialize(Create(repositoryRoot));
    }

    public static Oblivion.Presentation.Presentation Create(string? repositoryRoot = null)
    {
        return Oblivion.Presentation.Presentation.Create(
            id: Id,
            title: "M19 semantic presentation architecture",
            content:
            [
                Summary(
                    "summary",
                    "Oblivion now accepts ordinary C# semantic content and deterministically materializes the existing Cards used by human and agent product surfaces.",
                    title: "Semantic presentation MVP"),
                Markdown(
                    "architecture-notes",
                    new PresentationSource(
                        """
                        # Meaning first, presentation downstream

                        `PresentationContent` owns authored meaning and stable identity. The materializer projects each item to one existing Oblivion Card. Mature Markdown, code, Mermaid, and PNG presenters continue to own reading behavior.

                        Layout is a separate set of relationships by content ID. With no relationship, content remains a vertical semantic stream in authored order.
                        """,
                        "docs/Oblivion/oblivion-semantic-presentation-authoring-m19f.md"),
                    title: "Architecture notes"),
                Diagram(
                    "architecture-flow",
                    new DiagramSource.Mermaid(new PresentationSource(
                        """
                        flowchart LR
                            A[Ordinary C#] --> B[PresentationContent]
                            B --> C[PresentationMaterializer]
                            C --> D[Oblivion Cards]
                            D --> E[Mature content presenters]
                            E --> F[Machina host]
                        """,
                        "presentation:m19-architecture/architecture-flow.mmd")),
                    title: "Authoring to human briefing"),
                Code(
                    "authoring-source",
                    new PresentationSource(
                        AuthoringExcerpt,
                        "src/Oblivion/Oblivion.App/Presentation/M19PresentationDogfood.cs"),
                    language: "csharp",
                    title: "The C# Codex writes"),
                Decision(
                    "direction",
                    "Use a semantic content stream with optional Compare, Columns, and Focus groups that reference stable content IDs.",
                    ["architecture-notes", "architecture-flow"],
                    title: "Authoring direction"),
                NextActions(
                    "next",
                    [
                        "Keep the Presentation source code-authored for M19f.",
                        "Use dogfood evidence to choose one narrow M19g improvement.",
                    ],
                    title: "Next actions"),
            ],
            layout:
            [
                Compare("source-and-explanation", "authoring-source", "architecture-notes"),
                Focus("architecture-focus", "architecture-flow"),
            ]);
    }

    public static OblivionDiagramRenderResult RealizeDiagram(string? repositoryRoot = null)
    {
        string root = repositoryRoot ?? FindRepositoryRoot();
        MaterializedPresentation presentation = Materialize(root);
        Oblivion.Model.OblivionCard card = presentation.Page.Cards.Single(candidate =>
            candidate.Id == PresentationMaterializer.CreateCardId(
                presentation.Source.Id,
                new PresentationContentId("architecture-flow")));
        OblivionContentPresentationItem diagram = OblivionContentPresenterSelector
            .Select(card, new OblivionCardViewState(IsExpanded: true, BodyScrollOffset: 0))
            .Items
            .Single(item => item.PresenterKind == OblivionContentPresenterKind.ExternalMermaidRenderer);
        OblivionExternalMermaidRenderer renderer = new(
            OblivionMermaidRendererDiscovery.Discover(root));

        return renderer.Render(new OblivionDiagramRenderRequest(
            diagram.ContentId,
            diagram.Source,
            diagram.SourceReference,
            Path.Combine(root, "artifacts", "derived", "mermaid"),
            OblivionResolvedAppearance.Light,
            presentation.Workspace.Id.Value,
            presentation.Page.Id.Value,
            card.Id.Value));
    }

    public const string AuthoringExcerpt = """
        return Presentation.Create(
            id: "m19-architecture",
            title: "M19 architecture",
            content:
            [
                Summary("summary", summary),
                Markdown("notes", markdown),
                Diagram("flow", new DiagramSource.Mermaid(mermaid)),
                Code("source", code, language: "csharp"),
                Artifact("proof", briefingPng, kind: "png"),
                Decision("direction", decision),
                NextActions("next", actions)
            ]);
        """;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Oblivion.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Oblivion.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Copeland repository root.");
    }
}
