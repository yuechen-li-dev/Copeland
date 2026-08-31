using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.Standalone;

public sealed class OblivionStandaloneSurface
{
    private readonly OblivionCardHandlerRegistry _cardHandlers;
    private readonly OblivionStandaloneStyle _style;

    public OblivionStandaloneSurface(
        string? vaultRoot = null,
        OblivionStandaloneStyle? style = null)
    {
        _style = style ?? OblivionStandaloneStyles.Dark;
        OblivionApplication application = new();
        OblivionWorkspaceSessionOpenResult open = application.OpenWorkspace(
            vaultRoot ?? M19iStructuredVault.DefaultRoot);
        if (!open.Succeeded || open.Session is null)
        {
            throw new InvalidOperationException(
                "The standalone structured vault could not be opened:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, open.Diagnostics));
        }

        Workspace = open.Session.Workspace;
        Page = open.Session.ActivePage;
        Cards = ValidateMarkdownCards(Page);
        _cardHandlers = OblivionCardHandlerRegistry.CreateDefault();
        Session = open.Session.State;
    }

    public OblivionWorkspace Workspace { get; }

    public OblivionWorkspacePage Page { get; }

    public IReadOnlyList<OblivionCard> Cards { get; }

    public string PageId => Page.Id.Value;

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
                Workspace.Id.Value,
                localStateOverride: localState);
            OblivionCompactCardView cardView = built.CompactView with
            {
                Subtitle = _style.CardSubtitle,
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

        return OblivionStandaloneRenderer.Render(width, height, cardPresentations, _style);
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
                $"Interaction '{interaction.GetType().Name}' is outside the standalone Page stack."),
        };
    }

    private bool IsThisCard(string pageId, string cardId)
    {
        return string.Equals(pageId, PageId, StringComparison.Ordinal) &&
            Cards.Any(card => string.Equals(card.Id.Value, cardId, StringComparison.Ordinal));
    }

    private static IReadOnlyList<OblivionCard> ValidateMarkdownCards(OblivionWorkspacePage page)
    {
        if (page.Cards.Any(card => card.Body.Format != OblivionCardBodyFormat.CopelandMarkdown))
        {
            throw new InvalidOperationException("The standalone Page stack must contain only Markdown Cards.");
        }

        return page.Cards;
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
