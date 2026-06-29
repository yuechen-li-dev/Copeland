namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationState(
    string SelectedSectionId,
    IReadOnlyDictionary<string, string> SelectedTabBySectionId,
    IReadOnlyDictionary<string, double> ScrollOffsetByPageId)
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
            ScrollOffsetByPageId: new Dictionary<string, double>(StringComparer.Ordinal));
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
}
