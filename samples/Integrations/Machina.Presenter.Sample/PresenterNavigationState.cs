namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationState(
    string SelectedSectionId,
    IReadOnlyDictionary<string, string> SelectedTabBySectionId,
    IReadOnlyDictionary<string, double> PresenterScrollOffsetByPageId,
    OblivionSessionState OblivionSession,
    OblivionApplicationState OblivionApplication)
{
    public OblivionHostState OblivionHostState => new(OblivionSession, OblivionApplication);

    public static implicit operator OblivionHostState(PresenterNavigationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.OblivionHostState;
    }

    public OblivionCompactPane CompactPane => OblivionSession.InspectorPaneSelected
        ? OblivionCompactPane.Inspector
        : OblivionCompactPane.CardList;

    public OblivionEffectState EffectState => OblivionApplication.EffectState;

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

    public double GetInspectorScrollOffset(string pageId) => OblivionSession.GetInspectorScrollOffset(pageId);

    public string? GetSelectedCardId(string pageId, IReadOnlyList<OblivionCard> cards)
    {
        return OblivionSession.GetSelectedCardId(pageId, cards);
    }

    public double GetRawMarkdownSourceScrollOffset(string cardId) => OblivionSession.GetRawSourceScrollOffset(cardId);

    public OblivionCardViewState GetCardViewState(string pageId, string cardId)
    {
        return OblivionSession.GetCardViewState(pageId, cardId);
    }

}
