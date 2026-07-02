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
                .WithSelectedSection(sectionId)
                .WithCompactPane(PresenterCompactPane.CardList);
        }

        if (PresenterNavigationActions.TryParseSelectTab(actionId, out string tabSectionId, out string tabIdToSelect))
        {
            PresenterNavigationTab? tab = model.FindTab(tabSectionId, tabIdToSelect);
            if (tab is null)
            {
                return state;
            }

            return state
                .WithSelectedTab(tabSectionId, tabIdToSelect)
                .WithCompactPane(PresenterCompactPane.CardList);
        }

        if (PresenterNavigationActions.TryParseSetScrollOffset(actionId, out string pageId, out double requestedOffset))
        {
            if (!model.ContainsPage(pageId))
            {
                return state;
            }

            double contentHeight = PresenterNavigationCatalog.GetPageContentHeight(pageId, proofOptions, state);
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

        if (PresenterNavigationActions.TryParseToggleOblivionCardExpansion(actionId, out string expansionPageId, out string expansionCardId))
        {
            if (!model.ContainsPage(expansionPageId))
            {
                return state;
            }

            IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(expansionPageId, proofOptions);
            string resolvedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(expansionPageId, expansionCardId, proofOptions);
            if (cards.All(card => !string.Equals(card.Id.Value, resolvedCardId, StringComparison.Ordinal)))
            {
                return state;
            }

            return state
                .WithSelectedCard(expansionPageId, resolvedCardId)
                .ToggleCardExpansion(expansionPageId, resolvedCardId);
        }

        if (PresenterNavigationActions.TryParseCollapseOblivionCard(actionId, out string collapsePageId, out string collapseCardId))
        {
            if (!model.ContainsPage(collapsePageId))
            {
                return state;
            }

            string resolvedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(collapsePageId, collapseCardId, proofOptions);
            return state.CollapseCard(collapsePageId, resolvedCardId);
        }

        if (PresenterNavigationActions.TryParseSetOblivionCardBodyScrollOffset(
            actionId,
            out string bodyScrollPageId,
            out string bodyScrollCardId,
            out double requestedBodyScrollOffset))
        {
            if (!model.ContainsPage(bodyScrollPageId))
            {
                return state;
            }

            string resolvedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(bodyScrollPageId, bodyScrollCardId, proofOptions);
            double clamped = OblivionWorkbenchCatalog.ClampBodyScrollOffset(
                bodyScrollPageId,
                resolvedCardId,
                requestedBodyScrollOffset,
                proofOptions,
                state,
                layout);
            return state
                .WithSelectedCard(bodyScrollPageId, resolvedCardId)
                .WithCardBodyScrollOffset(bodyScrollPageId, resolvedCardId, clamped);
        }

        if (PresenterNavigationActions.TryParseSelectCompactOblivionCard(actionId, out string compactPageId, out string compactCardId))
        {
            if (!model.ContainsPage(compactPageId))
            {
                return state;
            }

            IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(compactPageId, proofOptions);
            string resolvedCardId = OblivionWorkbenchCatalog.ResolveCardSelectionId(compactPageId, compactCardId, proofOptions);
            if (cards.All(card => !string.Equals(card.Id.Value, resolvedCardId, StringComparison.Ordinal)))
            {
                return state;
            }

            return state
                .WithSelectedCard(compactPageId, resolvedCardId)
                .WithCompactPane(PresenterCompactPane.Inspector);
        }

        if (PresenterNavigationActions.TryParseClearOblivionCardSelection(actionId, out string clearPageId))
        {
            if (!model.ContainsPage(clearPageId))
            {
                return state;
            }

            return state.ClearSelectedCard(clearPageId);
        }

        if (PresenterNavigationActions.TryParseSetCompactPane(actionId, out PresenterCompactPane compactPane))
        {
            return state.WithCompactPane(compactPane);
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
