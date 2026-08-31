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
        Presentation = M19hTwoCardStack.Materialize();
        Cards = AssertTwoCards(Presentation);
        _cardHandlers = OblivionCardHandlerRegistry.CreateDefault();
        Session = OblivionSessionState.Empty.ReconcilePage(PageId, Cards);
    }

    public MaterializedPresentation Presentation { get; }

    public IReadOnlyList<OblivionCard> Cards { get; }

    public string PageId => Presentation.Page.Id.Value;

    public OblivionSessionState Session { get; private set; }

    public string? SelectedCardId => Session.GetSelectedCardId(PageId, Cards);

    public bool AreAllCardsExpanded => Cards.All(IsExpanded);

    public bool IsExpanded(OblivionCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return Session.GetCardViewState(PageId, card.Id.Value).IsExpanded;
    }

    public OblivionStandaloneSurfaceSnapshot CreateSnapshot(int width, int height)
    {
        List<OblivionStandaloneCardPresentation> cardPresentations = [];
        foreach (OblivionCard card in Cards)
        {
            OblivionCardViewState state = Session.GetCardViewState(PageId, card.Id.Value);
            OblivionCardLocalState localState = OblivionCardLocalState.CreateDefault(card.Id) with
            {
                IsExpanded = state.IsExpanded,
                BodyScrollOffset = state.BodyScrollOffset,
            };
            OblivionBuiltCard built = _cardHandlers.BuildCard(
                card,
                PageId,
                Presentation.Workspace.Id.Value,
                localStateOverride: localState);
            OblivionCompactCardView cardView = built.CompactView with
            {
                Subtitle = OblivionStandaloneStyles.M19h.CardSubtitle,
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
                card,
                state);
            bool isSelected = string.Equals(
                SelectedCardId,
                card.Id.Value,
                StringComparison.Ordinal);
            cardPresentations.Add(new OblivionStandaloneCardPresentation(
                card,
                cardView,
                contentPlan,
                isSelected));
        }

        return OblivionStandaloneRenderer.Render(width, height, cardPresentations);
    }

    public void ToggleExpansion(string cardId)
    {
        Dispatch(OblivionUiActions.ToggleCardExpansion(PageId, cardId));
    }

    public void Collapse(string cardId)
    {
        Dispatch(OblivionUiActions.CollapseCard(PageId, cardId));
    }

    public void Select(string cardId)
    {
        Dispatch(OblivionUiActions.SelectCard(PageId, cardId));
    }

    public void SetPageScrollOffset(double offset)
    {
        Session = Session.WithMainScrollOffset(PageId, Math.Max(0, offset));
    }

    public void Dispatch(UiActionId actionId)
    {
        if (!OblivionUiActions.TryDecode(actionId, out OblivionInteraction? interaction) || interaction is null)
        {
            throw new InvalidOperationException($"Action '{actionId.Value}' is not an Oblivion interaction.");
        }

        Session = interaction switch
        {
            OblivionInteraction.SelectCard select when IsThisCard(select.PageId, select.CardId) =>
                Session.WithSelectedCard(select.PageId, select.CardId),
            OblivionInteraction.ToggleCardExpansion toggle when IsThisCard(toggle.PageId, toggle.CardId) =>
                Session.ToggleCardExpansion(toggle.PageId, toggle.CardId),
            OblivionInteraction.CollapseCard collapse when IsThisCard(collapse.PageId, collapse.CardId) =>
                Session.CollapseCard(collapse.PageId, collapse.CardId),
            _ => throw new InvalidOperationException(
                $"Interaction '{interaction.GetType().Name}' is outside the standalone two-card surface."),
        };
    }

    private bool IsThisCard(string pageId, string cardId)
    {
        return string.Equals(pageId, PageId, StringComparison.Ordinal) &&
            Cards.Any(card => string.Equals(card.Id.Value, cardId, StringComparison.Ordinal));
    }

    private static IReadOnlyList<OblivionCard> AssertTwoCards(MaterializedPresentation presentation)
    {
        if (presentation.Page.Cards.Count != 2)
        {
            throw new InvalidOperationException(
                $"The M19h standalone presentation must materialize exactly two cards, but produced {presentation.Page.Cards.Count}.");
        }

        return presentation.Page.Cards;
    }
}

public sealed record OblivionStandaloneCardPresentation(
    OblivionCard Card,
    OblivionCompactCardView CardView,
    OblivionContentPresentationPlan ContentPlan,
    bool IsSelected);

public sealed record OblivionStandaloneCardSnapshot(
    OblivionCard Card,
    OblivionCompactCardView CardView,
    OblivionContentPresentationPlan ContentPlan,
    bool IsSelected,
    Rect CardBounds,
    Rect ExpansionAffordanceBounds,
    Rect? MatureContentBounds)
{
    public bool MatureContentMounted => MatureContentBounds is not null;
}

public sealed record OblivionStandaloneSurfaceSnapshot(
    int Width,
    int ViewportHeight,
    int PageContentHeight,
    Aurelian.Rendering.Raster.RasterFrame ShellFrame,
    IReadOnlyList<OblivionStandaloneCardSnapshot> Cards);
