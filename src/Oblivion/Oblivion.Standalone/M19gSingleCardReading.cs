using Oblivion.Presentation;
using static Oblivion.Presentation.Content;

namespace Oblivion.Standalone;

public static class M19gSingleCardReading
{
    public const string PresentationId = "m19g-single-card-reading";
    public const string ContentId = "reading-surface";

    public static MaterializedPresentation Materialize()
    {
        Presentation.Presentation source = Presentation.Presentation.Create(
            id: PresentationId,
            title: "Oblivion",
            content:
            [
                Markdown(
                    ContentId,
                    new PresentationSource(
                        MarkdownSource,
                        "docs/Oblivion/oblivion-single-card-reading-mvp-m19g.md"),
                    title: "A notebook begins with one readable card"),
            ]);

        return PresentationMaterializer.Materialize(source);
    }

    public const string MarkdownSource = """
        # The physical atom of Oblivion

        Oblivion is an executable technical notebook: authored as semantic content, presented as a focused reading surface, and kept inspectable by the systems around it. This milestone deliberately starts with the smallest trustworthy interaction rather than another gallery of possibilities.

        ## What this baseline proves

        - The application uses the full viewport while the prose keeps a comfortable reading width.
        - Machina owns the page, vertical card stack, card frame, title, state, spacing, and the square expand or collapse affordance.
        - Avalonia owns the expanded document's font metrics, word wrapping, line spacing, paragraph rhythm, selection, and measurement.
        - Collapsing removes the document body completely; it does not squeeze the prose into a miniature preview.

        The boundary stays explicit: semantic `PresentationContent` becomes an Oblivion Card and presenter plan before the host realizes a mature read-only document control. Product models remain independent of Avalonia.

        ## Why start here

        A notebook can accumulate comparisons, diagrams, artifacts, and executable material later. None of those additions matter if the basic transition from scan to read feels cramped, ambiguous, or synthetic. The single card therefore carries the whole milestone: its collapsed state is fast to scan, its expanded state is calm enough to read, and neither state exposes implementation tooling.

        The wide shell and restrained document column solve different problems. The shell establishes that Oblivion is the application, not a panel inside another product. The reading column keeps conventional line lengths instead of stretching prose across every available pixel.

        ## Reading contract

        The expanded card grows to a screen-relative bound. The document uses a centered reading column inside the wide card. If future content exceeds the available height, the single Avalonia document scroller owns that overflow, avoiding nested page and card scrolling in this MVP.

        This is intentionally one card. The card still lives in a normal vertical stack, so a later second card would be another stack item rather than a new shell architecture.
        """;
}
