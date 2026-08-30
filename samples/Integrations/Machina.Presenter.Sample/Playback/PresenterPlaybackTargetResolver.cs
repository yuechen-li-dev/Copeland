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
            "main-stack" => ResolveScrollRegion(render, OblivionScrollTargetKind.MainCardStack, cardId, target),
            "card-header" => ResolveCardHeader(render, cardId),
            "expanded-body" => ResolveScrollRegion(render, OblivionScrollTargetKind.ExpandedMarkdownBody, cardId, target),
            "inspector-pane" => ResolveScrollRegion(render, OblivionScrollTargetKind.InspectorPane, cardId, target),
            "raw-source" => ResolveScrollRegion(render, OblivionScrollTargetKind.InspectorRawMarkdownSource, cardId, target),
            "main-stack-scrollbar-thumb" => ResolveScrollbarThumb(render, OblivionScrollTargetKind.MainCardStack, cardId, target),
            "expanded-body-scrollbar-thumb" => ResolveScrollbarThumb(render, OblivionScrollTargetKind.ExpandedMarkdownBody, cardId, target),
            "inspector-scrollbar-thumb" => ResolveScrollbarThumb(render, OblivionScrollTargetKind.InspectorPane, cardId, target),
            "raw-source-scrollbar-thumb" => ResolveScrollbarThumb(render, OblivionScrollTargetKind.InspectorRawMarkdownSource, cardId, target),
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
            null,
            "card-header",
            $"{render.SelectedTab.PageId}.{resolvedCardId}.card-header");
    }

    private static PresenterPlaybackResolvedTarget ResolveScrollRegion(
        PresenterNavigationShellRenderResult render,
        OblivionScrollTargetKind kind,
        string? cardId,
        string targetName)
    {
        OblivionScrollRegionTarget region = FindScrollRegion(render, kind, cardId, targetName);
        Rect rootBounds = TranslateScrollRegionToRoot(render, region.Target, region.Bounds);
        Rect visibleBounds = Intersect(rootBounds, render.ChromeGeometry.ContentViewportRect);
        if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
        {
            throw new InvalidOperationException(
                $"Playback target '{targetName}' resolves to '{BuildRegionId(region.Target)}', but that region is outside the visible presenter content viewport in the current state.");
        }

        ScrollbarGeometry rootScrollbarGeometry = TranslateScrollbarGeometryToRoot(render, region.Target, region.ScrollbarGeometry);
        return new PresenterPlaybackResolvedTarget(
            targetName,
            region.Target.CardId,
            visibleBounds,
            Center(visibleBounds),
            region.Target,
            rootScrollbarGeometry,
            BuildRegionKind(region.Target.Kind),
            BuildRegionId(region.Target));
    }

    private static PresenterPlaybackResolvedTarget ResolveScrollbarThumb(
        PresenterNavigationShellRenderResult render,
        OblivionScrollTargetKind kind,
        string? cardId,
        string targetName)
    {
        OblivionScrollRegionTarget region = FindScrollRegion(render, kind, cardId, targetName);
        if (!region.ScrollbarGeometry.IsVisible)
        {
            throw new InvalidOperationException($"Playback target '{targetName}' is unavailable because its scrollbar is not visible.");
        }

        ScrollbarGeometry rootScrollbarGeometry = TranslateScrollbarGeometryToRoot(render, region.Target, region.ScrollbarGeometry);
        return new PresenterPlaybackResolvedTarget(
            targetName,
            region.Target.CardId,
            rootScrollbarGeometry.ThumbRect,
            Center(rootScrollbarGeometry.ThumbRect),
            region.Target,
            rootScrollbarGeometry,
            BuildRegionKind(region.Target.Kind),
            BuildRegionId(region.Target));
    }

    private static OblivionScrollRegionTarget FindScrollRegion(
        PresenterNavigationShellRenderResult render,
        OblivionScrollTargetKind kind,
        string? cardId,
        string targetName)
    {
        string? resolvedCardId = kind is OblivionScrollTargetKind.ExpandedMarkdownBody or OblivionScrollTargetKind.InspectorRawMarkdownSource
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

        IReadOnlyList<OblivionCard> cards = OblivionWorkbench.GetPageCardsForSelection(pageId, render.ProofOptions);
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

    private static Rect TranslateScrollRegionToRoot(
        PresenterNavigationShellRenderResult render,
        OblivionScrollTarget target,
        Rect rect)
    {
        double translatedY = rect.Y;
        if (target.Kind == OblivionScrollTargetKind.ExpandedMarkdownBody)
        {
            translatedY -= render.NavigationState.GetScrollOffset(render.SelectedTab.PageId);
        }

        if (target.Kind == OblivionScrollTargetKind.InspectorRawMarkdownSource)
        {
            translatedY -= render.NavigationState.GetInspectorScrollOffset(render.SelectedTab.PageId);
        }

        return new Rect(
            render.ChromeGeometry.ContentViewportRect.X + rect.X,
            render.ChromeGeometry.ContentViewportRect.Y + translatedY,
            rect.Width,
            rect.Height);
    }

    private static ScrollbarGeometry TranslateScrollbarGeometryToRoot(
        PresenterNavigationShellRenderResult render,
        OblivionScrollTarget target,
        ScrollbarGeometry geometry)
    {
        return new ScrollbarGeometry(
            TrackRect: TranslateScrollRegionToRoot(render, target, geometry.TrackRect),
            ThumbRect: TranslateScrollRegionToRoot(render, target, geometry.ThumbRect),
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

    private static Rect Intersect(Rect left, Rect right)
    {
        double x = Math.Max(left.X, right.X);
        double y = Math.Max(left.Y, right.Y);
        double maxX = Math.Min(left.X + left.Width, right.X + right.Width);
        double maxY = Math.Min(left.Y + left.Height, right.Y + right.Height);
        double width = Math.Max(0, maxX - x);
        double height = Math.Max(0, maxY - y);
        return new Rect(x, y, width, height);
    }

    private static string BuildRegionKind(OblivionScrollTargetKind kind)
    {
        return kind switch
        {
            OblivionScrollTargetKind.MainCardStack => "oblivion-main-card-stack",
            OblivionScrollTargetKind.ExpandedMarkdownBody => "oblivion-expanded-markdown-body",
            OblivionScrollTargetKind.InspectorPane => "oblivion-inspector-pane",
            OblivionScrollTargetKind.InspectorRawMarkdownSource => "oblivion-inspector-raw-markdown-source",
            _ => "unknown",
        };
    }

    private static string BuildRegionId(OblivionScrollTarget target)
    {
        return target.Kind switch
        {
            OblivionScrollTargetKind.MainCardStack => $"{target.PageId}.main-stack",
            OblivionScrollTargetKind.ExpandedMarkdownBody => $"{target.PageId}.{target.CardId}.expanded-body",
            OblivionScrollTargetKind.InspectorPane => $"{target.PageId}.inspector-pane",
            OblivionScrollTargetKind.InspectorRawMarkdownSource => $"{target.PageId}.{target.CardId}.raw-source",
            _ => $"{target.PageId}.unknown",
        };
    }
}
