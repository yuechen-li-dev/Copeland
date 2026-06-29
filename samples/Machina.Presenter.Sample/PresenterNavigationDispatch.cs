using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationDispatch
{
    public static PresenterNavigationState Dispatch(
        PresenterNavigationState state,
        UiActionId actionId,
        PresenterNavigationModel model,
        PresenterProofOptions proofOptions,
        PresenterNavigationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(proofOptions);
        ArgumentNullException.ThrowIfNull(layout);

        if (PresenterNavigationActions.TryParseSelectSection(actionId, out string sectionId))
        {
            PresenterNavigationSection? section = model.FindSection(sectionId);
            if (section is null)
            {
                return state;
            }

            string tabId = state.GetSelectedTabId(sectionId, model);
            if (section.Tabs.All(tab => !string.Equals(tab.Id, tabId, StringComparison.Ordinal)))
            {
                tabId = section.Tabs[0].Id;
            }

            return state
                .WithSelectedTab(sectionId, tabId)
                .WithSelectedSection(sectionId);
        }

        if (PresenterNavigationActions.TryParseSelectTab(actionId, out string tabSectionId, out string tabIdToSelect))
        {
            PresenterNavigationTab? tab = model.FindTab(tabSectionId, tabIdToSelect);
            if (tab is null)
            {
                return state;
            }

            return state.WithSelectedTab(tabSectionId, tabIdToSelect);
        }

        if (PresenterNavigationActions.TryParseSetScrollOffset(actionId, out string pageId, out double requestedOffset))
        {
            if (!model.ContainsPage(pageId))
            {
                return state;
            }

            double contentHeight = PresenterNavigationCatalog.GetPageContentHeight(pageId, proofOptions);
            double clamped = PresenterScrollRegion.ClampScrollOffset(contentHeight, layout.ViewportHeight, requestedOffset);
            return state.WithScrollOffset(pageId, clamped);
        }

        return state;
    }
}
