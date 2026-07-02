namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationState(
    string SelectedSectionId,
    IReadOnlyDictionary<string, string> SelectedTabBySectionId,
    IReadOnlyDictionary<string, double> ScrollOffsetByPageId,
    IReadOnlyDictionary<string, string?> SelectedCardByPageId,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>> CardViewStateByPageId,
    PresenterCompactPane CompactPane,
    OblivionCardEffectState EffectState)
{
    public static PresenterNavigationState CreateDefault(PresenterNavigationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var selectedTabs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PresenterNavigationSection section in model.Sections)
        {
            selectedTabs[section.Id] = section.Tabs[0].Id;
        }

        return new PresenterNavigationState(
            SelectedSectionId: model.Sections[0].Id,
            SelectedTabBySectionId: selectedTabs,
            ScrollOffsetByPageId: new Dictionary<string, double>(StringComparer.Ordinal),
            SelectedCardByPageId: new Dictionary<string, string?>(StringComparer.Ordinal),
            CardViewStateByPageId: new Dictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>>(StringComparer.Ordinal),
            CompactPane: PresenterCompactPane.CardList,
            EffectState: OblivionCardEffectState.Empty);
    }

    public string GetSelectedTabId(string sectionId, PresenterNavigationModel model)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        ArgumentNullException.ThrowIfNull(model);

        if (SelectedTabBySectionId.TryGetValue(sectionId, out string? tabId))
        {
            PresenterNavigationSection? section = model.FindSection(sectionId);
            if (section is not null && section.Tabs.Any(tab => string.Equals(tab.Id, tabId, StringComparison.Ordinal)))
            {
                return tabId;
            }
        }

        PresenterNavigationSection fallbackSection = model.FindSection(sectionId) ?? model.Sections[0];
        return fallbackSection.Tabs[0].Id;
    }

    public double GetScrollOffset(string pageId)
    {
        ArgumentNullException.ThrowIfNull(pageId);

        if (ScrollOffsetByPageId.TryGetValue(pageId, out double offset))
        {
            return offset;
        }

        return 0;
    }

    public PresenterNavigationState WithSelectedSection(string sectionId)
    {
        return this with
        {
            SelectedSectionId = sectionId,
        };
    }

    public PresenterNavigationState WithSelectedTab(string sectionId, string tabId)
    {
        var tabs = new Dictionary<string, string>(SelectedTabBySectionId, StringComparer.Ordinal)
        {
            [sectionId] = tabId,
        };

        return this with
        {
            SelectedSectionId = sectionId,
            SelectedTabBySectionId = tabs,
        };
    }

    public PresenterNavigationState WithScrollOffset(string pageId, double offset)
    {
        var offsets = new Dictionary<string, double>(ScrollOffsetByPageId, StringComparer.Ordinal)
        {
            [pageId] = offset,
        };

        return this with
        {
            ScrollOffsetByPageId = offsets,
        };
    }

    public PresenterNavigationState WithCompactPane(PresenterCompactPane compactPane)
    {
        return this with
        {
            CompactPane = compactPane,
        };
    }

    public string? GetSelectedCardId(string pageId, IReadOnlyList<OblivionCard> cards)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(cards);

        if (SelectedCardByPageId.TryGetValue(pageId, out string? selectedCardId))
        {
            if (selectedCardId is null)
            {
                return null;
            }

            if (cards.Any(card => string.Equals(card.Id.Value, selectedCardId, StringComparison.Ordinal)))
            {
                return selectedCardId;
            }
        }

        return cards.Count == 0
            ? null
            : cards[0].Id.Value;
    }

    public PresenterNavigationState WithSelectedCard(string pageId, string cardId)
    {
        var selectedCards = new Dictionary<string, string?>(SelectedCardByPageId, StringComparer.Ordinal)
        {
            [pageId] = cardId,
        };

        return this with
        {
            SelectedCardByPageId = selectedCards,
        };
    }

    public OblivionCardViewState GetCardViewState(string pageId, string cardId)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(cardId);

        if (CardViewStateByPageId.TryGetValue(pageId, out IReadOnlyDictionary<string, OblivionCardViewState>? pageState) &&
            pageState.TryGetValue(cardId, out OblivionCardViewState? viewState))
        {
            return viewState;
        }

        return OblivionCardViewState.Collapsed;
    }

    public PresenterNavigationState WithCardViewState(string pageId, string cardId, OblivionCardViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(pageId);
        ArgumentNullException.ThrowIfNull(cardId);
        ArgumentNullException.ThrowIfNull(viewState);

        Dictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>> nextPages = new(CardViewStateByPageId, StringComparer.Ordinal);
        Dictionary<string, OblivionCardViewState> nextCardState = CardViewStateByPageId.TryGetValue(pageId, out IReadOnlyDictionary<string, OblivionCardViewState>? existing)
            ? new Dictionary<string, OblivionCardViewState>(existing, StringComparer.Ordinal)
            : new Dictionary<string, OblivionCardViewState>(StringComparer.Ordinal);

        nextCardState[cardId] = viewState;
        nextPages[pageId] = nextCardState;

        return this with
        {
            CardViewStateByPageId = nextPages,
        };
    }

    public PresenterNavigationState ToggleCardExpansion(string pageId, string cardId)
    {
        OblivionCardViewState current = GetCardViewState(pageId, cardId);
        return WithCardViewState(
            pageId,
            cardId,
            current with
            {
                IsExpanded = !current.IsExpanded,
            });
    }

    public PresenterNavigationState CollapseCard(string pageId, string cardId)
    {
        OblivionCardViewState current = GetCardViewState(pageId, cardId);
        if (!current.IsExpanded)
        {
            return this;
        }

        return WithCardViewState(
            pageId,
            cardId,
            current with
            {
                IsExpanded = false,
            });
    }

    public PresenterNavigationState WithCardBodyScrollOffset(string pageId, string cardId, double bodyScrollOffset)
    {
        OblivionCardViewState current = GetCardViewState(pageId, cardId);
        return WithCardViewState(
            pageId,
            cardId,
            current with
            {
                BodyScrollOffset = bodyScrollOffset,
            });
    }

    public PresenterNavigationState ClearSelectedCard(string pageId)
    {
        var selectedCards = new Dictionary<string, string?>(SelectedCardByPageId, StringComparer.Ordinal)
        {
            [pageId] = null,
        };

        return this with
        {
            SelectedCardByPageId = selectedCards,
        };
    }

    public PresenterNavigationState WithEffectOutcome(
        OblivionCardEffectRequest request,
        OblivionCardEffectResult result)
    {
        return this with
        {
            EffectState = EffectState.WithOutcome(request, result),
        };
    }
}
