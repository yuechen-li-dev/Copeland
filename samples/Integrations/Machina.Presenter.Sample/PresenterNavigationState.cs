namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationState(
    string SelectedSectionId,
    IReadOnlyDictionary<string, string> SelectedTabBySectionId,
    IReadOnlyDictionary<string, double> PresenterScrollOffsetByPageId,
    OblivionSessionState OblivionSession,
    OblivionApplicationState OblivionApplication)
{
    public PresenterCompactPane CompactPane => OblivionSession.InspectorPaneSelected
        ? PresenterCompactPane.Inspector
        : PresenterCompactPane.CardList;

    public OblivionCardEffectState EffectState => OblivionApplication.EffectState;

    public static PresenterNavigationState CreateDefault(PresenterNavigationModel model)
    {
        Dictionary<string, string> selectedTabs = new(StringComparer.Ordinal);
        foreach (PresenterNavigationSection section in model.Sections)
        {
            selectedTabs[section.Id] = section.Tabs[0].Id;
        }

        return new PresenterNavigationState(
            model.Sections[0].Id,
            selectedTabs,
            new Dictionary<string, double>(StringComparer.Ordinal),
            OblivionSessionState.Empty,
            OblivionApplicationState.Empty);
    }

    public string GetSelectedTabId(string sectionId, PresenterNavigationModel model)
    {
        if (SelectedTabBySectionId.TryGetValue(sectionId, out string? tabId))
        {
            PresenterNavigationSection? section = model.FindSection(sectionId);
            if (section is not null && section.Tabs.Any(tab => tab.Id == tabId))
            {
                return tabId;
            }
        }

        return (model.FindSection(sectionId) ?? model.Sections[0]).Tabs[0].Id;
    }

    public double GetScrollOffset(string pageId)
    {
        if (PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            return OblivionSession.GetMainScrollOffset(pageId);
        }

        return PresenterScrollOffsetByPageId.TryGetValue(pageId, out double offset) ? offset : 0;
    }

    public PresenterNavigationState WithSelectedSection(string sectionId)
    {
        return this with { SelectedSectionId = sectionId };
    }

    public PresenterNavigationState WithSelectedTab(string sectionId, string tabId)
    {
        Dictionary<string, string> tabs = new(SelectedTabBySectionId, StringComparer.Ordinal)
        {
            [sectionId] = tabId,
        };
        return this with { SelectedSectionId = sectionId, SelectedTabBySectionId = tabs };
    }

    public PresenterNavigationState WithScrollOffset(string pageId, double offset)
    {
        if (PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            return this with { OblivionSession = OblivionSession.WithMainScrollOffset(pageId, offset) };
        }

        Dictionary<string, double> offsets = new(PresenterScrollOffsetByPageId, StringComparer.Ordinal)
        {
            [pageId] = offset,
        };
        return this with { PresenterScrollOffsetByPageId = offsets };
    }

    public PresenterNavigationState WithCompactPane(PresenterCompactPane pane)
    {
        return this with
        {
            OblivionSession = OblivionSession with
            {
                InspectorPaneSelected = pane == PresenterCompactPane.Inspector,
            },
        };
    }

    public double GetInspectorScrollOffset(string pageId) => OblivionSession.GetInspectorScrollOffset(pageId);

    public PresenterNavigationState WithInspectorScrollOffset(string pageId, double offset)
    {
        return this with { OblivionSession = OblivionSession.WithInspectorScrollOffset(pageId, offset) };
    }

    public string? GetSelectedCardId(string pageId, IReadOnlyList<OblivionCard> cards)
    {
        return OblivionSession.GetSelectedCardId(pageId, cards);
    }

    public PresenterNavigationState WithSelectedCard(string pageId, string cardId)
    {
        return this with { OblivionSession = OblivionSession.WithSelectedCard(pageId, cardId) };
    }

    public double GetRawMarkdownSourceScrollOffset(string cardId) => OblivionSession.GetRawSourceScrollOffset(cardId);

    public PresenterNavigationState WithRawMarkdownSourceScrollOffset(string cardId, double offset)
    {
        return this with { OblivionSession = OblivionSession.WithRawSourceScrollOffset(cardId, offset) };
    }

    public OblivionCardViewState GetCardViewState(string pageId, string cardId)
    {
        return OblivionSession.GetCardViewState(pageId, cardId);
    }

    public PresenterNavigationState WithCardViewState(string pageId, string cardId, OblivionCardViewState state)
    {
        return this with { OblivionSession = OblivionSession.WithCardViewState(pageId, cardId, state) };
    }

    public PresenterNavigationState ToggleCardExpansion(string pageId, string cardId)
    {
        return this with { OblivionSession = OblivionSession.ToggleCardExpansion(pageId, cardId) };
    }

    public PresenterNavigationState ExpandCardExclusively(
        string pageId,
        string cardId,
        IReadOnlyList<string> siblingCardIds)
    {
        return this with
        {
            OblivionSession = OblivionSession.ExpandCardExclusively(pageId, cardId, siblingCardIds),
        };
    }

    public PresenterNavigationState CollapseCard(string pageId, string cardId)
    {
        return this with { OblivionSession = OblivionSession.CollapseCard(pageId, cardId) };
    }

    public PresenterNavigationState WithCardBodyScrollOffset(string pageId, string cardId, double offset)
    {
        return this with { OblivionSession = OblivionSession.WithCardBodyScrollOffset(pageId, cardId, offset) };
    }

    public PresenterNavigationState ClearSelectedCard(string pageId)
    {
        return this with { OblivionSession = OblivionSession.ClearSelectedCard(pageId) };
    }

    public PresenterNavigationState WithEffectOutcome(
        OblivionCardEffectRequest request,
        OblivionCardEffectResult result)
    {
        return this with { OblivionApplication = OblivionApplication.Apply(request, result) };
    }
}
