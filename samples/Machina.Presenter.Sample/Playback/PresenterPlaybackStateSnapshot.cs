namespace Machina.Presenter.Sample.Playback;

public sealed record PresenterPlaybackStateSnapshot(
    string SelectedSection,
    string SelectedTab,
    string PageId,
    string? SelectedCard,
    IReadOnlyList<string> ExpandedCards,
    double MainStackScrollOffset,
    double? ExpandedBodyScrollOffset,
    double InspectorScrollOffset,
    double? RawSourceScrollOffset,
    string? CapturedScrollRegion,
    PresenterShellMode ShellMode,
    int PageRenderCount,
    int ShellRenderCount,
    int CompositionCount)
{
    public static PresenterPlaybackStateSnapshot Capture(
        PresenterNavigationShellRenderResult render,
        string? cardId = null,
        string? capturedScrollRegion = null)
    {
        ArgumentNullException.ThrowIfNull(render);

        string pageId = render.SelectedTab.PageId;
        IReadOnlyList<OblivionCard> cards = PresenterNavigationCatalog.IsOblivionPage(pageId)
            ? OblivionWorkbenchCatalog.GetPageCardsForSelection(pageId, render.ProofOptions)
            : [];
        string? selectedCard = PresenterNavigationCatalog.IsOblivionPage(pageId)
            ? render.NavigationState.GetSelectedCardId(pageId, cards)
            : null;
        string? effectiveCardId = cardId ?? selectedCard;
        IReadOnlyList<string> expandedCards = PresenterNavigationCatalog.IsOblivionPage(pageId)
            ? cards
                .Where(card => render.NavigationState.GetCardViewState(pageId, card.Id.Value).IsExpanded)
                .Select(card => card.Id.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
            : [];

        double? expandedBodyScrollOffset = null;
        double? rawSourceScrollOffset = null;
        if (!string.IsNullOrWhiteSpace(effectiveCardId))
        {
            expandedBodyScrollOffset = render.NavigationState.GetCardViewState(pageId, effectiveCardId).BodyScrollOffset;
            rawSourceScrollOffset = render.NavigationState.GetRawMarkdownSourceScrollOffset(effectiveCardId);
        }

        return new PresenterPlaybackStateSnapshot(
            SelectedSection: render.SelectedSection.Id,
            SelectedTab: render.SelectedTab.Id,
            PageId: pageId,
            SelectedCard: selectedCard,
            ExpandedCards: expandedCards,
            MainStackScrollOffset: render.NavigationState.GetScrollOffset(pageId),
            ExpandedBodyScrollOffset: expandedBodyScrollOffset,
            InspectorScrollOffset: render.NavigationState.GetInspectorScrollOffset(pageId),
            RawSourceScrollOffset: rawSourceScrollOffset,
            CapturedScrollRegion: capturedScrollRegion,
            ShellMode: render.ShellMode,
            PageRenderCount: render.Diagnostics.PageRenderCount,
            ShellRenderCount: render.Diagnostics.ShellRenderCount,
            CompositionCount: render.Diagnostics.CompositionCount);
    }
}
