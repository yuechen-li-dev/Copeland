using Machina.Core.Actions;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

public static class PresenterKeyboardRouter
{
    public const double SmallScrollDelta = 48;
    public const double PageScrollFactor = 0.9;

    public static PresenterNavigationInputRoutingResult Route(
        PresenterNavigationShellRenderResult render,
        UiInputEvent inputEvent,
        ScrollbarInteractionState interactionState)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(inputEvent);
        ArgumentNullException.ThrowIfNull(interactionState);

        if (inputEvent is not UiKeyChanged keyChanged)
        {
            return new PresenterNavigationInputRoutingResult(
                PresenterNavigationHitTarget.None,
                null,
                interactionState,
                PointerCaptureRequest.None,
                SuppressFurtherRouting: false);
        }

        UiActionId? actionId = keyChanged.IsPressed
            ? RouteKeyDown(render, keyChanged)
            : null;

        return new PresenterNavigationInputRoutingResult(
            PresenterNavigationHitTarget.None,
            actionId,
            interactionState,
            PointerCaptureRequest.None,
            SuppressFurtherRouting: false);
    }

    private static UiActionId? RouteKeyDown(
        PresenterNavigationShellRenderResult render,
        UiKeyChanged keyboard)
    {
        string pageId = render.SelectedTab.PageId;

        if (IsCtrlChord(keyboard, UiKey.ArrowDown))
        {
            return SelectAdjacentSection(render, direction: 1);
        }

        if (IsCtrlChord(keyboard, UiKey.ArrowUp))
        {
            return SelectAdjacentSection(render, direction: -1);
        }

        if (IsCtrlChord(keyboard, UiKey.ArrowRight))
        {
            return SelectAdjacentTab(render, direction: 1);
        }

        if (IsCtrlChord(keyboard, UiKey.ArrowLeft))
        {
            return SelectAdjacentTab(render, direction: -1);
        }

        if (IsCtrlChord(keyboard, UiKey.R))
        {
            return InvokeSelectedCardAction(render);
        }

        if (HasNonCtrlModifiers(keyboard.Modifiers))
        {
            return null;
        }

        return keyboard.Key switch
        {
            UiKey.Enter => ToggleSelectedCardExpansion(render),
            UiKey.Space => ToggleSelectedCardExpansion(render),
            UiKey.ArrowDown => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset + SmallScrollDelta),
            UiKey.ArrowUp => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset - SmallScrollDelta),
            UiKey.PageDown => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset + (render.Layout.ViewportHeight * PageScrollFactor)),
            UiKey.PageUp => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset - (render.Layout.ViewportHeight * PageScrollFactor)),
            UiKey.Home => PresenterNavigationActions.SetScrollOffset(pageId, 0),
            UiKey.End => PresenterNavigationActions.SetScrollOffset(pageId, render.ScrollbarGeometry.MaxScrollOffset),
            UiKey.Escape => RouteEscape(render),
            _ => null,
        };
    }

    private static UiActionId? SelectAdjacentSection(
        PresenterNavigationShellRenderResult render,
        int direction)
    {
        int currentIndex = FindSectionIndex(render.Model, render.SelectedSection.Id);
        if (currentIndex < 0)
        {
            return null;
        }

        int nextIndex = Math.Clamp(currentIndex + direction, 0, render.Model.Sections.Count - 1);
        string nextSectionId = render.Model.Sections[nextIndex].Id;
        return string.Equals(nextSectionId, render.SelectedSection.Id, StringComparison.Ordinal)
            ? null
            : PresenterNavigationActions.SelectSection(nextSectionId);
    }

    private static UiActionId? SelectAdjacentTab(
        PresenterNavigationShellRenderResult render,
        int direction)
    {
        IReadOnlyList<PresenterNavigationTab> tabs = render.SelectedSection.Tabs;
        int currentIndex = tabs
            .Select((tab, index) => (tab, index))
            .FirstOrDefault(candidate => string.Equals(candidate.tab.Id, render.SelectedTab.Id, StringComparison.Ordinal))
            .index;

        int nextIndex = Math.Clamp(currentIndex + direction, 0, tabs.Count - 1);
        string nextTabId = tabs[nextIndex].Id;
        return string.Equals(nextTabId, render.SelectedTab.Id, StringComparison.Ordinal)
            ? null
            : PresenterNavigationActions.SelectTab(render.SelectedSection.Id, nextTabId);
    }

    private static UiActionId? ToggleSelectedCardExpansion(PresenterNavigationShellRenderResult render)
    {
        string pageId = render.SelectedTab.PageId;
        if (!PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            return null;
        }

        IReadOnlyList<OblivionCard> cards = OblivionWorkbench.GetPageCardsForSelection(pageId, render.ProofOptions);
        string? selectedCardId = render.NavigationState.GetSelectedCardId(pageId, cards);
        return selectedCardId is null
            ? null
            : OblivionUiActions.ToggleCardExpansion(pageId, selectedCardId);
    }

    private static UiActionId? RouteEscape(PresenterNavigationShellRenderResult render)
    {
        string pageId = render.SelectedTab.PageId;
        if (PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            IReadOnlyList<OblivionCard> cards = OblivionWorkbench.GetPageCardsForSelection(pageId, render.ProofOptions);
            string? selectedCardId = render.NavigationState.GetSelectedCardId(pageId, cards);
            if (selectedCardId is not null &&
                render.NavigationState.GetCardViewState(pageId, selectedCardId).IsExpanded)
            {
                return OblivionUiActions.CollapseCard(pageId, selectedCardId);
            }
        }

        return ClearSelectedCard(render);
    }

    private static UiActionId? ClearSelectedCard(PresenterNavigationShellRenderResult render)
    {
        string pageId = render.SelectedTab.PageId;
        if (!PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            return null;
        }

        if (render.Layout.ShellMode == PresenterShellMode.Compact &&
            render.NavigationState.CompactPane == OblivionCompactPane.Inspector)
        {
            return OblivionUiActions.SetCompactPane(OblivionCompactPane.CardList);
        }

        IReadOnlyList<OblivionCard> cards = OblivionWorkbench.GetPageCardsForSelection(pageId, render.ProofOptions);
        string? selectedCardId = render.NavigationState.GetSelectedCardId(pageId, cards);
        return selectedCardId is null
            ? null
            : OblivionUiActions.ClearCardSelection(pageId);
    }

    private static UiActionId? InvokeSelectedCardAction(PresenterNavigationShellRenderResult render)
    {
        string pageId = render.SelectedTab.PageId;
        if (!PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            return null;
        }

        IReadOnlyList<OblivionBuiltCard> cards = OblivionWorkbench.GetBuiltPageCardsForSelection(
            pageId,
            render.ProofOptions,
            render.NavigationState.EffectState);
        string? selectedCardId = render.NavigationState.GetSelectedCardId(
            pageId,
            cards.Select(card => card.SourceCard).ToArray());
        if (selectedCardId is null)
        {
            return null;
        }

        OblivionBuiltCard? selectedCard = cards.FirstOrDefault(card =>
            string.Equals(card.SourceCard.Id.Value, selectedCardId, StringComparison.Ordinal));
        OblivionCardActionDescriptor? action = selectedCard?.RuntimeModel.Actions.FirstOrDefault(candidate =>
            candidate.RequiresEffect);

        return action is null
            ? null
            : OblivionUiActions.InvokeProductAction(pageId, selectedCardId, action.Id);
    }

    private static bool IsCtrlChord(UiKeyChanged keyboard, UiKey key)
    {
        return keyboard.Key == key &&
               keyboard.Modifiers.Control &&
               !keyboard.Modifiers.Alt &&
               !keyboard.Modifiers.Meta;
    }

    private static bool HasNonCtrlModifiers(UiModifiers modifiers)
    {
        return modifiers.Alt || modifiers.Meta || modifiers.Control || modifiers.Shift;
    }

    private static int FindSectionIndex(PresenterNavigationModel model, string sectionId)
    {
        for (int index = 0; index < model.Sections.Count; index++)
        {
            if (string.Equals(model.Sections[index].Id, sectionId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
