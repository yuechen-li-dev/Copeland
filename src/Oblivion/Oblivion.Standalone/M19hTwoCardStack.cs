using Oblivion.Presentation;
using static Oblivion.Presentation.Content;

namespace Oblivion.Standalone;

public static class M19hTwoCardStack
{
    public const string PresentationId = "m19h-two-card-stack";
    public const string FirstContentId = "physical-atom";
    public const string SecondContentId = "notebook-stack";

    public static MaterializedPresentation Materialize()
    {
        Presentation.Presentation source = Presentation.Presentation.Create(
            id: PresentationId,
            title: "Oblivion",
            content:
            [
                Markdown(
                    FirstContentId,
                    new PresentationSource(
                        FirstMarkdownSource,
                        "docs/Oblivion/oblivion-two-card-stack-mvp-m19h.md"),
                    title: "The physical atom of Oblivion"),
                Markdown(
                    SecondContentId,
                    new PresentationSource(
                        SecondMarkdownSource,
                        "docs/Oblivion/oblivion-two-card-stack-mvp-m19h.md"),
                    title: "From one card to a notebook stack"),
            ]);

        return PresentationMaterializer.Materialize(source);
    }

    public const string FirstMarkdownSource = """
        # The physical atom of Oblivion

        A trustworthy notebook begins with a readable physical atom. Each Card carries semantic Markdown through the same product pipeline, while the standalone shell stays quiet enough for the document to remain the point.

        ## The contract

        - Machina owns the Card frame, header, spacing, state, and square affordance.
        - Avalonia owns mature type metrics, wrapping, selection, and document measurement.
        - Collapsing removes the body instead of manufacturing a miniature preview.

        The wide shell establishes application scale. A centered prose column independently protects readable line length, so resizing the window changes available space without changing the meaning or order of the content.
        """;

    public const string SecondMarkdownSource = """
        # From one card to a notebook stack

        The second Card is intentionally ordinary. It uses the same shell, the same Markdown presenter, and the same session-state rules as the first Card. Its presence qualifies the stack rather than introducing another content kind.

        ## What the stack proves

        - Card order remains deterministic from materialization through recomposition.
        - Either Card can expand or collapse without changing the other Card's state.
        - Normal vertical layout moves later Cards when an earlier Card changes height.
        - The page owns overflow when the combined stack is taller than the viewport.

        Selection is restrained and independent from expansion. After a resize, the selected Card and both expansion states survive while widths and vertical positions are recomputed from the current viewport.
        """;
}
