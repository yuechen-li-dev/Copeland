namespace Oblivion.Product;

public enum OblivionCompactPane
{
    CardList,
    Inspector,
}

public sealed record OblivionSessionState(
    IReadOnlyDictionary<string, double> MainScrollOffsetByPageId,
    IReadOnlyDictionary<string, double> InspectorScrollOffsetByPageId,
    IReadOnlyDictionary<string, string?> SelectedCardByPageId,
    IReadOnlyDictionary<string, double> RawSourceScrollOffsetByCardId,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>> CardViewStateByPageId,
    IReadOnlyDictionary<string, OblivionViewportState> ViewportStateByPageId,
    IReadOnlyDictionary<string, OblivionDiagramViewportState> DiagramViewportStateByCardId,
    bool InspectorPaneSelected)
{
    public static OblivionSessionState Empty { get; } = new(
        new Dictionary<string, double>(StringComparer.Ordinal),
        new Dictionary<string, double>(StringComparer.Ordinal),
        new Dictionary<string, string?>(StringComparer.Ordinal),
        new Dictionary<string, double>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>>(StringComparer.Ordinal),
        new Dictionary<string, OblivionViewportState>(StringComparer.Ordinal),
        new Dictionary<string, OblivionDiagramViewportState>(StringComparer.Ordinal),
        InspectorPaneSelected: false);

    public double GetMainScrollOffset(string pageId) => GetOffset(MainScrollOffsetByPageId, pageId);
    public double GetInspectorScrollOffset(string pageId) => GetOffset(InspectorScrollOffsetByPageId, pageId);
    public double GetRawSourceScrollOffset(string cardId) => GetOffset(RawSourceScrollOffsetByCardId, cardId);

    public OblivionSessionState WithMainScrollOffset(string pageId, double offset)
    {
        return this with { MainScrollOffsetByPageId = SetOffset(MainScrollOffsetByPageId, pageId, offset) };
    }

    public OblivionSessionState WithInspectorScrollOffset(string pageId, double offset)
    {
        return this with { InspectorScrollOffsetByPageId = SetOffset(InspectorScrollOffsetByPageId, pageId, offset) };
    }

    public OblivionSessionState WithRawSourceScrollOffset(string cardId, double offset)
    {
        return this with { RawSourceScrollOffsetByCardId = SetOffset(RawSourceScrollOffsetByCardId, cardId, offset) };
    }

    public string? GetSelectedCardId(string pageId, IReadOnlyList<OblivionCard> cards)
    {
        if (SelectedCardByPageId.TryGetValue(pageId, out string? selectedCardId))
        {
            return selectedCardId is null || cards.Any(card => card.Id.Value == selectedCardId)
                ? selectedCardId
                : cards.FirstOrDefault()?.Id.Value;
        }

        return cards.FirstOrDefault()?.Id.Value;
    }

    public OblivionSessionState WithSelectedCard(string pageId, string cardId)
    {
        string? current = SelectedCardByPageId.TryGetValue(pageId, out string? selected) ? selected : null;
        Dictionary<string, string?> selections = new(SelectedCardByPageId, StringComparer.Ordinal)
        {
            [pageId] = cardId,
        };
        OblivionSessionState next = this with { SelectedCardByPageId = selections };
        return current == cardId ? next : next.WithInspectorScrollOffset(pageId, 0);
    }

    public OblivionSessionState ClearSelectedCard(string pageId)
    {
        Dictionary<string, string?> selections = new(SelectedCardByPageId, StringComparer.Ordinal)
        {
            [pageId] = null,
        };
        return this with { SelectedCardByPageId = selections };
    }

    public OblivionSessionState ReconcilePage(
        string pageId,
        IReadOnlyList<OblivionCard> cards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentNullException.ThrowIfNull(cards);

        HashSet<string> cardIds = cards
            .Select(card => card.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        string? selectedCardId = GetSelectedCardId(pageId, cards);

        Dictionary<string, string?> selections = new(SelectedCardByPageId, StringComparer.Ordinal)
        {
            [pageId] = selectedCardId,
        };
        Dictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>> pageStates =
            new(CardViewStateByPageId, StringComparer.Ordinal);
        if (pageStates.TryGetValue(pageId, out IReadOnlyDictionary<string, OblivionCardViewState>? existing))
        {
            pageStates[pageId] = existing
                .Where(pair => cardIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        return this with
        {
            SelectedCardByPageId = selections,
            CardViewStateByPageId = pageStates,
        };
    }

    public OblivionCardViewState GetCardViewState(string pageId, string cardId)
    {
        return CardViewStateByPageId.TryGetValue(pageId, out IReadOnlyDictionary<string, OblivionCardViewState>? page) &&
            page.TryGetValue(cardId, out OblivionCardViewState? state)
                ? state
                : OblivionCardViewState.Collapsed;
    }

    public OblivionSessionState WithCardViewState(string pageId, string cardId, OblivionCardViewState state)
    {
        Dictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>> pages = new(CardViewStateByPageId, StringComparer.Ordinal);
        Dictionary<string, OblivionCardViewState> cards = CardViewStateByPageId.TryGetValue(pageId, out IReadOnlyDictionary<string, OblivionCardViewState>? existing)
            ? new Dictionary<string, OblivionCardViewState>(existing, StringComparer.Ordinal)
            : new Dictionary<string, OblivionCardViewState>(StringComparer.Ordinal);
        cards[cardId] = state;
        pages[pageId] = cards;
        return this with { CardViewStateByPageId = pages };
    }

    public OblivionSessionState ToggleCardExpansion(string pageId, string cardId)
    {
        OblivionCardViewState current = GetCardViewState(pageId, cardId);
        return WithCardViewState(pageId, cardId, current with { IsExpanded = !current.IsExpanded });
    }

    public OblivionSessionState ExpandCardExclusively(string pageId, string cardId, IReadOnlyList<string> siblingCardIds)
    {
        OblivionSessionState next = this;
        foreach (string siblingCardId in siblingCardIds)
        {
            OblivionCardViewState current = next.GetCardViewState(pageId, siblingCardId);
            bool expanded = siblingCardId == cardId;
            if (current.IsExpanded != expanded)
            {
                next = next.WithCardViewState(pageId, siblingCardId, current with { IsExpanded = expanded });
            }
        }

        return next;
    }

    public OblivionSessionState CollapseCard(string pageId, string cardId)
    {
        OblivionCardViewState current = GetCardViewState(pageId, cardId);
        return current.IsExpanded
            ? WithCardViewState(pageId, cardId, current with { IsExpanded = false })
            : this;
    }

    public OblivionSessionState WithCardBodyScrollOffset(string pageId, string cardId, double offset)
    {
        OblivionCardViewState current = GetCardViewState(pageId, cardId);
        return WithCardViewState(pageId, cardId, current with { BodyScrollOffset = offset });
    }

    public OblivionViewportState GetViewportState(string pageId)
    {
        return ViewportStateByPageId.TryGetValue(pageId, out OblivionViewportState? state)
            ? state
            : OblivionViewportState.Single;
    }

    public OblivionSessionState WithViewportState(string pageId, OblivionViewportState state)
    {
        Dictionary<string, OblivionViewportState> states = new(ViewportStateByPageId, StringComparer.Ordinal)
        {
            [pageId] = state,
        };
        return this with { ViewportStateByPageId = states };
    }

    public OblivionDiagramViewportState GetDiagramViewportState(string cardId)
    {
        return DiagramViewportStateByCardId.TryGetValue(cardId, out OblivionDiagramViewportState? state)
            ? state
            : OblivionDiagramViewportState.Fit;
    }

    public OblivionSessionState WithDiagramViewportState(
        string cardId,
        OblivionDiagramViewportState state)
    {
        Dictionary<string, OblivionDiagramViewportState> states =
            new(DiagramViewportStateByCardId, StringComparer.Ordinal)
            {
                [cardId] = state,
            };
        return this with { DiagramViewportStateByCardId = states };
    }

    private static double GetOffset(IReadOnlyDictionary<string, double> offsets, string id)
    {
        return offsets.TryGetValue(id, out double offset) ? offset : 0;
    }

    private static IReadOnlyDictionary<string, double> SetOffset(
        IReadOnlyDictionary<string, double> offsets,
        string id,
        double offset)
    {
        Dictionary<string, double> next = new(offsets, StringComparer.Ordinal) { [id] = offset };
        return next;
    }
}
