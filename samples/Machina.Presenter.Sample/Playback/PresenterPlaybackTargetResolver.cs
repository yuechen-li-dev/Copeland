using Machina.Layout.Geometry;

namespace Machina.Presenter.Sample.Playback;

public static class PresenterPlaybackTargetResolver
{
    public static PresenterPlaybackResolvedTarget Resolve(
        PresenterNavigationShellRenderResult render,
        string target,
        string? cardId = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        return target switch
        {
            "main-stack" => ResolveScrollRegion(render, PresenterScrollbarTargetKind.OblivionMainCardStack, cardId, target),
            "card-header" => ResolveCardHeader(render, cardId),
            "expanded-body" => ResolveScrollRegion(render, PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody, cardId, target),
            "inspector-pane" => ResolveScrollRegion(render, PresenterScrollbarTargetKind.OblivionInspectorPane, cardId, target),
            "raw-source" => ResolveScrollRegion(render, PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource, cardId, target),
            "main-stack-scrollbar-thumb" => ResolveScrollbarThumb(render, PresenterScrollbarTargetKind.OblivionMainCardStack, cardId, target),
            "expanded-body-scrollbar-thumb" => ResolveScrollbarThumb(render, PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody, cardId, target),
            "inspector-scrollbar-thumb" => ResolveScrollbarThumb(render, PresenterScrollbarTargetKind.OblivionInspectorPane, cardId, target),
            "raw-source-scrollbar-thumb" => ResolveScrollbarThumb(render, PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource, cardId, target),
            _ => throw new InvalidOperationException($"Playback target '{target}' is not supported."),
        };
    }

    private static PresenterPlaybackResolvedTarget ResolveCardHeader(
        PresenterNavigationShellRenderResult render,
        string? cardId)
    {
        string resolvedCardId = ResolveRequiredCardId(render, cardId, "card-header");
        OblivionCardHitTarget cardTarget = render.PageRender?.OblivionInteraction?.CardTargets
            .FirstOrDefault(target => string.Equals(target.CardId, resolvedCardId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Playback target 'card-header' is unavailable for card '{resolvedCardId}'.");

        Rect rootBounds = TranslateCardRectToRoot(render, cardTarget.HeaderBounds);
        return new PresenterPlaybackResolvedTarget(
            "card-header",
            resolvedCardId,
            rootBounds,
            Center(rootBounds),
            null,
            null);
    }

    private static PresenterPlaybackResolvedTarget ResolveScrollRegion(
        PresenterNavigationShellRenderResult render,
        PresenterScrollbarTargetKind kind,
        string? cardId,
        string targetName)
    {
        OblivionScrollRegionTarget region = FindScrollRegion(render, kind, cardId, targetName);
        Rect rootBounds = TranslateToRoot(render, region.Bounds);
        ScrollbarGeometry rootScrollbarGeometry = TranslateToRoot(render, region.ScrollbarGeometry);
        return new PresenterPlaybackResolvedTarget(
            targetName,
            region.Target.CardId,
            rootBounds,
            Center(rootBounds),
            region.Target,
            rootScrollbarGeometry);
    }

    private static PresenterPlaybackResolvedTarget ResolveScrollbarThumb(
        PresenterNavigationShellRenderResult render,
        PresenterScrollbarTargetKind kind,
        string? cardId,
        string targetName)
    {
        OblivionScrollRegionTarget region = FindScrollRegion(render, kind, cardId, targetName);
        if (!region.ScrollbarGeometry.IsVisible)
        {
            throw new InvalidOperationException($"Playback target '{targetName}' is unavailable because its scrollbar is not visible.");
        }

        ScrollbarGeometry rootScrollbarGeometry = TranslateToRoot(render, region.ScrollbarGeometry);
        return new PresenterPlaybackResolvedTarget(
            targetName,
            region.Target.CardId,
            rootScrollbarGeometry.ThumbRect,
            Center(rootScrollbarGeometry.ThumbRect),
            region.Target,
            rootScrollbarGeometry);
    }

    private static OblivionScrollRegionTarget FindScrollRegion(
        PresenterNavigationShellRenderResult render,
        PresenterScrollbarTargetKind kind,
        string? cardId,
        string targetName)
    {
        string? resolvedCardId = kind is PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody or PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource
            ? ResolveRequiredCardId(render, cardId, targetName)
            : cardId;

        return render.PageRender?.OblivionInteraction?.ScrollRegions
            .FirstOrDefault(region =>
                region.Target.Kind == kind &&
                (resolvedCardId is null || string.Equals(region.Target.CardId, resolvedCardId, StringComparison.Ordinal)))
            ?? throw new InvalidOperationException(
                resolvedCardId is null
                    ? $"Playback target '{targetName}' is unavailable in the current presenter state."
                    : $"Playback target '{targetName}' is unavailable for card '{resolvedCardId}' in the current presenter state.");
    }

    private static string ResolveRequiredCardId(PresenterNavigationShellRenderResult render, string? cardId, string targetName)
    {
        if (!string.IsNullOrWhiteSpace(cardId))
        {
            return cardId;
        }

        string pageId = render.SelectedTab.PageId;
        if (!PresenterNavigationCatalog.IsOblivionPage(pageId))
        {
            throw new InvalidOperationException($"Playback target '{targetName}' requires an Oblivion page.");
        }

        IReadOnlyList<OblivionCard> cards = OblivionWorkbenchCatalog.GetPageCardsForSelection(pageId, render.ProofOptions);
        return render.NavigationState.GetSelectedCardId(pageId, cards)
            ?? throw new InvalidOperationException($"Playback target '{targetName}' requires a selected card.");
    }

    private static Rect TranslateToRoot(PresenterNavigationShellRenderResult render, Rect rect)
    {
        return new Rect(
            render.ChromeGeometry.ContentViewportRect.X + rect.X,
            render.ChromeGeometry.ContentViewportRect.Y + rect.Y,
            rect.Width,
            rect.Height);
    }

    private static Rect TranslateCardRectToRoot(PresenterNavigationShellRenderResult render, Rect rect)
    {
        double mainStackScrollOffset = render.NavigationState.GetScrollOffset(render.SelectedTab.PageId);
        return new Rect(
            render.ChromeGeometry.ContentViewportRect.X + rect.X,
            render.ChromeGeometry.ContentViewportRect.Y + rect.Y - mainStackScrollOffset,
            rect.Width,
            rect.Height);
    }

    private static ScrollbarGeometry TranslateToRoot(PresenterNavigationShellRenderResult render, ScrollbarGeometry geometry)
    {
        return new ScrollbarGeometry(
            TrackRect: TranslateToRoot(render, geometry.TrackRect),
            ThumbRect: TranslateToRoot(render, geometry.ThumbRect),
            IsVisible: geometry.IsVisible,
            ScrollOffset: geometry.ScrollOffset,
            MaxScrollOffset: geometry.MaxScrollOffset);
    }

    private static PresenterPlaybackPoint Center(Rect rect)
    {
        return new PresenterPlaybackPoint(
            rect.X + (rect.Width / 2),
            rect.Y + (rect.Height / 2));
    }
}
