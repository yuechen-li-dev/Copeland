namespace Machina.Presenter.Sample;

public sealed record PresenterNavigationTab(
    string Id,
    string Label,
    string PageId);

public sealed record PresenterNavigationSection(
    string Id,
    string Label,
    IReadOnlyList<PresenterNavigationTab> Tabs);

public sealed record PresenterNavigationModel(
    IReadOnlyList<PresenterNavigationSection> Sections)
{
    public PresenterNavigationSection? FindSection(string sectionId)
    {
        return Sections.FirstOrDefault(section => string.Equals(section.Id, sectionId, StringComparison.Ordinal));
    }

    public PresenterNavigationTab? FindTab(string sectionId, string tabId)
    {
        PresenterNavigationSection? section = FindSection(sectionId);
        return section?.Tabs.FirstOrDefault(tab => string.Equals(tab.Id, tabId, StringComparison.Ordinal));
    }

    public PresenterNavigationSection FindSectionByPageId(string pageId)
    {
        foreach (PresenterNavigationSection section in Sections)
        {
            if (section.Tabs.Any(tab => string.Equals(tab.PageId, pageId, StringComparison.Ordinal)))
            {
                return section;
            }
        }

        throw new InvalidOperationException($"Unknown presenter page id '{pageId}'.");
    }

    public PresenterNavigationTab FindTabByPageId(string pageId)
    {
        foreach (PresenterNavigationSection section in Sections)
        {
            PresenterNavigationTab? tab = section.Tabs.FirstOrDefault(candidate => string.Equals(candidate.PageId, pageId, StringComparison.Ordinal));
            if (tab is not null)
            {
                return tab;
            }
        }

        throw new InvalidOperationException($"Unknown presenter page id '{pageId}'.");
    }

    public bool ContainsPage(string pageId)
    {
        return Sections.SelectMany(section => section.Tabs).Any(tab => string.Equals(tab.PageId, pageId, StringComparison.Ordinal));
    }
}
