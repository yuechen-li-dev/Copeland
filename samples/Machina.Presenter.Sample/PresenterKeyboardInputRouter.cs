using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public static class PresenterKeyboardInputRouter
{
    public const double SmallScrollDelta = 48;
    public const double PageScrollFactor = 0.9;

    public static PresenterNavigationInputRoutingResult Route(
        PresenterNavigationShellRenderResult render,
        PresenterInputEvent inputEvent,
        PresenterScrollbarInteractionState interactionState)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(inputEvent);
        ArgumentNullException.ThrowIfNull(interactionState);

        PresenterKeyboardInput? keyboard = inputEvent.Keyboard;
        if (keyboard is null)
        {
            return new PresenterNavigationInputRoutingResult(
                PresenterNavigationHitTarget.None,
                null,
                interactionState,
                PresenterPointerCaptureRequest.None,
                SuppressFurtherRouting: false);
        }

        UiActionId? actionId = inputEvent.Kind switch
        {
            PresenterInputKind.KeyDown => RouteKeyDown(render, keyboard),
            PresenterInputKind.KeyUp => null,
            PresenterInputKind.TextInput => null,
            _ => null,
        };

        return new PresenterNavigationInputRoutingResult(
            PresenterNavigationHitTarget.None,
            actionId,
            interactionState,
            PresenterPointerCaptureRequest.None,
            SuppressFurtherRouting: false);
    }

    private static UiActionId? RouteKeyDown(
        PresenterNavigationShellRenderResult render,
        PresenterKeyboardInput keyboard)
    {
        string pageId = render.SelectedTab.PageId;

        if (IsCtrlChord(keyboard, PresenterKey.ArrowDown))
        {
            return SelectAdjacentSection(render, direction: 1);
        }

        if (IsCtrlChord(keyboard, PresenterKey.ArrowUp))
        {
            return SelectAdjacentSection(render, direction: -1);
        }

        if (IsCtrlChord(keyboard, PresenterKey.ArrowRight))
        {
            return SelectAdjacentTab(render, direction: 1);
        }

        if (IsCtrlChord(keyboard, PresenterKey.ArrowLeft))
        {
            return SelectAdjacentTab(render, direction: -1);
        }

        if (IsCtrlChord(keyboard, PresenterKey.R))
        {
            return InvokeSelectedCardAction(render);
        }

        if (HasNonCtrlModifiers(keyboard.Modifiers))
        {
            return null;
        }

        return keyboard.Key switch
        {
            PresenterKey.ArrowDown => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset + SmallScrollDelta),
            PresenterKey.ArrowUp => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset - SmallScrollDelta),
            PresenterKey.PageDown => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset + (render.Layout.ViewportHeight * PageScrollFactor)),
            PresenterKey.PageUp => PresenterNavigationActions.SetScrollOffset(
                pageId,
                render.ScrollbarGeometry.ScrollOffset - (render.Layout.ViewportHeight * PageScrollFactor)),
            PresenterKey.Home => PresenterNavigationActions.SetScrollOffset(pageId, 0),
            PresenterKey.End => PresenterNavigationActions.SetScrollOffset(pageId, render.ScrollbarGeometry.MaxScrollOffset),
            PresenterKey.Escape => ClearSelectedCard(render),
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

    private static UiActionId? ClearSelectedCard(PresenterNavigationShellRenderResult render)
    {
        string pageId = render.SelectedTab.PageId;
        if (!PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            return null;
        }

        if (render.Layout.ShellMode == PresenterShellMode.Compact &&
            render.NavigationState.CompactPane == PresenterCompactPane.Inspector)
        {
            return PresenterNavigationActions.SetCompactPane(PresenterCompactPane.CardList);
        }

        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(pageId, render.ProofOptions);
        string? selectedCardId = render.NavigationState.GetSelectedCardId(pageId, cards);
        return selectedCardId is null
            ? null
            : PresenterNavigationActions.ClearOblivionCardSelection(pageId);
    }

    private static UiActionId? InvokeSelectedCardAction(PresenterNavigationShellRenderResult render)
    {
        string pageId = render.SelectedTab.PageId;
        if (!PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            return null;
        }

        IReadOnlyList<OblivionBuiltCard> cards = OblivionWorkbenchCatalog.GetBuiltPageCardsForSelection(
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
            : PresenterNavigationActions.InvokeOblivionCardAction(pageId, selectedCardId, action.Id);
    }

    private static bool IsCtrlChord(PresenterKeyboardInput keyboard, PresenterKey key)
    {
        return keyboard.Key == key &&
               keyboard.Modifiers.Ctrl &&
               !keyboard.Modifiers.Alt &&
               !keyboard.Modifiers.Meta;
    }

    private static bool HasNonCtrlModifiers(PresenterKeyModifiers modifiers)
    {
        return modifiers.Alt || modifiers.Meta || modifiers.Ctrl || modifiers.Shift;
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
