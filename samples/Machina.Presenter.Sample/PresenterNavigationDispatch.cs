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

        if (PresenterNavigationActions.TryParseSelectOblivionCard(actionId, out string oblivionPageId, out string cardId))
        {
            if (!model.ContainsPage(oblivionPageId))
            {
                return state;
            }

            IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(oblivionPageId, proofOptions);
            string resolvedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(oblivionPageId, cardId, proofOptions);
            if (cards.All(card => !string.Equals(card.Id.Value, resolvedCardId, StringComparison.Ordinal)))
            {
                return state;
            }

            return state.WithSelectedCard(oblivionPageId, resolvedCardId);
        }

        if (PresenterNavigationActions.TryParseClearOblivionCardSelection(actionId, out string clearPageId))
        {
            if (!model.ContainsPage(clearPageId))
            {
                return state;
            }

            return state.ClearSelectedCard(clearPageId);
        }

        if (PresenterNavigationActions.TryParseInvokeOblivionCardAction(actionId, out string actionPageId, out string actionCardId, out string cardActionId))
        {
            if (!model.ContainsPage(actionPageId))
            {
                return state;
            }

            IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(actionPageId, proofOptions);
            string resolvedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(actionPageId, actionCardId, proofOptions);
            OblivionCard? card = cards.FirstOrDefault(candidate =>
                string.Equals(candidate.Id.Value, resolvedCardId, StringComparison.Ordinal));
            if (card is null)
            {
                return state;
            }

            OblivionCardEffectOutcome? outcome = OblivionWorkbenchCatalog.InvokeCardAction(
                actionPageId,
                resolvedCardId,
                cardActionId,
                proofOptions,
                state.EffectState);
            if (outcome is null)
            {
                return state;
            }

            return state
                .WithSelectedCard(actionPageId, resolvedCardId)
                .WithEffectOutcome(outcome.Request, outcome.Result);
        }

        return state;
    }
}
