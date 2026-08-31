using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Oblivion.Model;
using Oblivion.Presentation;
using Oblivion.Product;

namespace Oblivion.Standalone;

public sealed class OblivionStandaloneSurface
{
    private readonly OblivionCardHandlerRegistry _cardHandlers;

    public OblivionStandaloneSurface()
    {
        Presentation = M19gSingleCardReading.Materialize();
        Card = AssertSingleCard(Presentation);
        _cardHandlers = OblivionCardHandlerRegistry.CreateDefault();
        Session = OblivionSessionState.Empty.ReconcilePage(PageId, [Card]);
    }

    public MaterializedPresentation Presentation { get; }

    public OblivionCard Card { get; }

    public string PageId => Presentation.Page.Id.Value;

    public OblivionSessionState Session { get; private set; }

    public bool IsExpanded => Session.GetCardViewState(PageId, Card.Id.Value).IsExpanded;

    public OblivionStandaloneSurfaceSnapshot CreateSnapshot(int width, int height)
    {
        OblivionCardViewState state = Session.GetCardViewState(PageId, Card.Id.Value);
        OblivionCardLocalState localState = OblivionCardLocalState.CreateDefault(Card.Id) with
        {
            IsExpanded = state.IsExpanded,
            BodyScrollOffset = state.BodyScrollOffset,
        };
        OblivionBuiltCard built = _cardHandlers.BuildCard(
            Card,
            PageId,
            Presentation.Workspace.Id.Value,
            localStateOverride: localState);
        OblivionCompactCardView cardView = built.CompactView with
        {
            Subtitle = "A standalone technical reading surface",
            SourceLabel = null,
            SummaryLine = null,
            MetaBadges = state.IsExpanded
                ? ["Markdown", "Expanded"]
                : ["Markdown", "Collapsed"],
            Tags = [],
            ActionBadges = [],
            ArtifactBadges = [],
        };
        OblivionContentPresentationPlan contentPlan = OblivionContentPresenterSelector.Select(
            Card,
            state);

        return OblivionStandaloneRenderer.Render(
            width,
            height,
            Card,
            cardView,
            contentPlan);
    }

    public void ToggleExpansion()
    {
        Dispatch(OblivionUiActions.ToggleCardExpansion(PageId, Card.Id.Value));
    }

    public void Collapse()
    {
        Dispatch(OblivionUiActions.CollapseCard(PageId, Card.Id.Value));
    }

    public void Dispatch(UiActionId actionId)
    {
        if (!OblivionUiActions.TryDecode(actionId, out OblivionInteraction? interaction) || interaction is null)
        {
            throw new InvalidOperationException($"Action '{actionId.Value}' is not an Oblivion interaction.");
        }

        Session = interaction switch
        {
            OblivionInteraction.ToggleCardExpansion toggle when IsThisCard(toggle.PageId, toggle.CardId) =>
                Session.ToggleCardExpansion(toggle.PageId, toggle.CardId),
            OblivionInteraction.CollapseCard collapse when IsThisCard(collapse.PageId, collapse.CardId) =>
                Session.CollapseCard(collapse.PageId, collapse.CardId),
            _ => throw new InvalidOperationException(
                $"Interaction '{interaction.GetType().Name}' is outside the standalone single-card surface."),
        };
    }

    private bool IsThisCard(string pageId, string cardId)
    {
        return string.Equals(pageId, PageId, StringComparison.Ordinal) &&
            string.Equals(cardId, Card.Id.Value, StringComparison.Ordinal);
    }

    private static OblivionCard AssertSingleCard(MaterializedPresentation presentation)
    {
        if (presentation.Page.Cards.Count != 1)
        {
            throw new InvalidOperationException(
                $"The M19g standalone presentation must materialize exactly one card, but produced {presentation.Page.Cards.Count}.");
        }

        return presentation.Page.Cards[0];
    }
}

public sealed record OblivionStandaloneSurfaceSnapshot(
    int Width,
    int Height,
    OblivionCard Card,
    OblivionCompactCardView CardView,
    OblivionContentPresentationPlan ContentPlan,
    Aurelian.Rendering.Raster.RasterFrame ShellFrame,
    Rect CardBounds,
    Rect ExpansionAffordanceBounds,
    Rect? MatureContentBounds)
{
    public bool MatureContentMounted => MatureContentBounds is not null;
}
