using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Machina.Runtime.Input;

namespace Oblivion.Product;

public enum OblivionScrollTargetKind
{
    MainCardStack,
    ExpandedMarkdownBody,
    InspectorPane,
    InspectorRawMarkdownSource,
}

public sealed record OblivionScrollTarget(
    OblivionScrollTargetKind Kind,
    string PageId,
    string? CardId = null)
{
    public ScrollbarInteractionTarget ToRuntimeTarget()
    {
        return new ScrollbarInteractionTarget($"{Kind}|{PageId}|{CardId}");
    }
}

public sealed record OblivionCardHitTarget(
    string PageId,
    string CardId,
    Rect Bounds,
    UiActionId ActionId,
    Rect HeaderBounds);

public sealed record OblivionCardBodyHitTarget(
    string PageId,
    string CardId,
    Rect Bounds,
    UiActionId SelectActionId,
    ScrollbarGeometry ScrollbarGeometry,
    double ContentHeight);

public sealed record OblivionScrollRegionTarget(
    OblivionScrollTarget Target,
    Rect Bounds,
    ScrollbarGeometry ScrollbarGeometry,
    double ContentHeight);

public sealed record OblivionPageInteractionRoutingResult(
    UiAction? Action,
    bool Consumed,
    ScrollbarInteractionState InteractionState,
    PointerCaptureRequest PointerCaptureRequest,
    OblivionInteractionHitResult? HitResult = null);

public sealed record OblivionInteractionHitResult(
    string RegionKind,
    string RegionId,
    string? CardId,
    string? ScrollRegionId,
    PointerPoint LocalPoint);

public sealed record OblivionPageInteractionMap(
    string PageId,
    IReadOnlyList<OblivionCardHitTarget> CardTargets,
    IReadOnlyList<OblivionCardBodyHitTarget> BodyTargets,
    IReadOnlyList<OblivionScrollRegionTarget> ScrollRegions)
{
    public UiAction? HitTest(PointerPoint point, double scrollOffset)
    {
        double contentX = point.X;
        double contentY = point.Y + scrollOffset;

        foreach (OblivionCardBodyHitTarget target in BodyTargets)
        {
            if (Contains(target.Bounds, contentX, contentY))
            {
                return new UiAction(target.SelectActionId);
            }
        }

        foreach (OblivionCardHitTarget target in CardTargets)
        {
            if (Contains(target.HeaderBounds, contentX, contentY) ||
                Contains(target.Bounds, contentX, contentY))
            {
                return new UiAction(target.ActionId);
            }
        }

        return null;
    }

    public OblivionPageInteractionRoutingResult RouteInput(
        UiInputEvent inputEvent,
        double pageScrollOffset,
        ScrollbarInteractionState interactionState)
    {
        if (!inputEvent.TryGetPointerPosition(out PointerPoint pointerPosition))
        {
            return new OblivionPageInteractionRoutingResult(
                null,
                Consumed: false,
                interactionState,
                PointerCaptureRequest.None,
                null);
        }

        double contentX = pointerPosition.X;
        double contentY = pointerPosition.Y + pageScrollOffset;

        if (interactionState is ScrollbarInteractionState.ThumbDragging dragging)
        {
            OblivionScrollRegionTarget? dragRegion = ScrollRegions.FirstOrDefault(
                region => region.Target.ToRuntimeTarget() == dragging.Target);
            if (dragRegion is not null)
            {
                return ReduceScrollInteraction(
                    dragRegion,
                    ScrollbarHitPart.None,
                    inputEvent,
                    interactionState);
            }
        }

        if (inputEvent.IsWheel(out _))
        {
            bool hoveredScrollableRegion = false;
            OblivionScrollRegionTarget? hoveredRegion = null;
            foreach (OblivionScrollRegionTarget target in EnumerateScrollableRegionsByPriority(contentX, contentY, pageScrollOffset))
            {
                hoveredScrollableRegion = true;
                hoveredRegion = target;
                OblivionPageInteractionRoutingResult reduced = ReduceScrollInteraction(
                    target,
                    ScrollbarHitPart.Viewport,
                    inputEvent,
                    interactionState);
                if (reduced.Action is not null || reduced.Consumed)
                {
                    return reduced;
                }
            }

            if (hoveredScrollableRegion)
            {
                return new OblivionPageInteractionRoutingResult(
                    null,
                    Consumed: true,
                    interactionState,
                    PointerCaptureRequest.None,
                    new OblivionInteractionHitResult(
                        RegionKind: "scroll-region",
                        RegionId: BuildScrollRegionId(hoveredRegion!.Target),
                        CardId: hoveredRegion.Target.CardId,
                        ScrollRegionId: BuildScrollRegionId(hoveredRegion.Target),
                        LocalPoint: pointerPosition));
            }
        }

        if (inputEvent.IsPrimaryPressed())
        {
            foreach (OblivionScrollRegionTarget target in EnumerateScrollableRegionsByPriority(contentX, contentY, pageScrollOffset))
            {
                ScrollbarHitPart hitPart = GetScrollbarHitPart(target, contentX, contentY, pageScrollOffset);
                if (hitPart == ScrollbarHitPart.None)
                {
                    continue;
                }

                OblivionPageInteractionRoutingResult reduced = ReduceScrollInteraction(target, hitPart, inputEvent, interactionState);
                if (reduced.Action is not null || reduced.Consumed)
                {
                    return reduced;
                }
            }

            foreach (OblivionCardBodyHitTarget target in BodyTargets)
            {
                if (Contains(target.Bounds, contentX, contentY))
                {
                    return new OblivionPageInteractionRoutingResult(
                        new UiAction(target.SelectActionId),
                        Consumed: true,
                        interactionState,
                        PointerCaptureRequest.None,
                        new OblivionInteractionHitResult(
                            RegionKind: "card-body",
                            RegionId: $"{target.PageId}.{target.CardId}.card-body",
                            CardId: target.CardId,
                            ScrollRegionId: null,
                            LocalPoint: pointerPosition));
                }
            }

            foreach (OblivionCardHitTarget target in CardTargets)
            {
                if (Contains(target.HeaderBounds, contentX, contentY) ||
                    Contains(target.Bounds, contentX, contentY))
                {
                    return new OblivionPageInteractionRoutingResult(
                        new UiAction(target.ActionId),
                        Consumed: true,
                        interactionState,
                        PointerCaptureRequest.None,
                        new OblivionInteractionHitResult(
                            RegionKind: Contains(target.HeaderBounds, contentX, contentY) ? "card-header" : "card",
                            RegionId: Contains(target.HeaderBounds, contentX, contentY)
                                ? $"{target.PageId}.{target.CardId}.card-header"
                                : $"{target.PageId}.{target.CardId}.card",
                            CardId: target.CardId,
                            ScrollRegionId: null,
                            LocalPoint: pointerPosition));
                }
            }
        }

        return new OblivionPageInteractionRoutingResult(
            null,
            Consumed: false,
            interactionState,
            PointerCaptureRequest.None,
            null);
    }

    public OblivionPageInteractionRoutingResult RouteInput(
        UiInputEvent inputEvent,
        double pageScrollOffset)
    {
        return RouteInput(inputEvent, pageScrollOffset, ScrollbarInteractionState.Default);
    }

    private IEnumerable<OblivionScrollRegionTarget> EnumerateScrollableRegionsByPriority(double x, double y, double pageScrollOffset)
    {
        double inspectorScrollOffset = ScrollRegions
            .FirstOrDefault(region => region.Target.Kind == OblivionScrollTargetKind.InspectorPane)?
            .ScrollbarGeometry.ScrollOffset
            ?? 0;

        return ScrollRegions
            .Where(target => ContainsInteractiveBounds(target, x, y, pageScrollOffset, inspectorScrollOffset))
            .OrderByDescending(target => GetPriority(target.Target.Kind))
            .ThenBy(target => target.Bounds.Area(), Comparer<double>.Create((left, right) => left.CompareTo(right)));
    }

    private static int GetPriority(OblivionScrollTargetKind kind)
    {
        return kind switch
        {
            OblivionScrollTargetKind.InspectorRawMarkdownSource => 4,
            OblivionScrollTargetKind.ExpandedMarkdownBody => 3,
            OblivionScrollTargetKind.InspectorPane => 2,
            OblivionScrollTargetKind.MainCardStack => 1,
            _ => 0,
        };
    }

    private ScrollbarHitPart GetScrollbarHitPart(OblivionScrollRegionTarget target, double x, double y, double pageScrollOffset)
    {
        double inspectorScrollOffset = ScrollRegions
            .FirstOrDefault(region => region.Target.Kind == OblivionScrollTargetKind.InspectorPane)?
            .ScrollbarGeometry.ScrollOffset
            ?? 0;
        ScrollbarGeometry visibleGeometry = GetVisibleScrollbarGeometry(target, pageScrollOffset, inspectorScrollOffset);

        if (target.ScrollbarGeometry.IsVisible &&
            (Contains(target.ScrollbarGeometry.ThumbRect, x, y) || Contains(visibleGeometry.ThumbRect, x, y)))
        {
            return ScrollbarHitPart.Thumb;
        }

        if (target.ScrollbarGeometry.IsVisible &&
            (Contains(target.ScrollbarGeometry.TrackRect, x, y) || Contains(visibleGeometry.TrackRect, x, y)))
        {
            return ScrollbarHitPart.Track;
        }

        if (ContainsInteractiveBounds(target, x, y, pageScrollOffset, inspectorScrollOffset))
        {
            return ScrollbarHitPart.Viewport;
        }

        return ScrollbarHitPart.None;
    }

    private static OblivionPageInteractionRoutingResult ReduceScrollInteraction(
        OblivionScrollRegionTarget target,
        ScrollbarHitPart hitPart,
        UiInputEvent inputEvent,
        ScrollbarInteractionState interactionState)
    {
        ScrollbarInteractionResult reduced = ScrollbarInteraction.Reduce(
            interactionState,
            new ScrollbarInteractionContext(
                target.Target.ToRuntimeTarget(),
                ToInteractionGeometry(target.ScrollbarGeometry),
                target.Bounds.Height),
            hitPart,
            inputEvent);

        return new OblivionPageInteractionRoutingResult(
            reduced.RequestedScrollOffset is null
                ? null
                : OblivionUiActions.SetScrollOffset(target.Target, reduced.RequestedScrollOffset.Value).ToAction(),
            reduced.Consumed,
            reduced.State,
            reduced.PointerCaptureRequest,
            new OblivionInteractionHitResult(
                RegionKind: GetScrollRegionKind(target.Target.Kind),
                RegionId: BuildScrollRegionId(target.Target),
                CardId: target.Target.CardId,
                ScrollRegionId: BuildScrollRegionId(target.Target),
                LocalPoint: inputEvent.TryGetPointerPosition(out PointerPoint position) ? position : default));
    }

    private static string GetScrollRegionKind(OblivionScrollTargetKind kind)
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

    private static string BuildScrollRegionId(OblivionScrollTarget target)
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

    private static bool ContainsInteractiveBounds(
        OblivionScrollRegionTarget target,
        double x,
        double y,
        double pageScrollOffset,
        double inspectorScrollOffset)
    {
        if (Contains(target.Bounds, x, y))
        {
            return true;
        }

        Rect visibleBounds = GetVisibleBounds(target, pageScrollOffset, inspectorScrollOffset);
        return Contains(visibleBounds, x, y);
    }

    private static Rect GetVisibleBounds(
        OblivionScrollRegionTarget target,
        double pageScrollOffset,
        double inspectorScrollOffset)
    {
        double deltaY = target.Target.Kind switch
        {
            OblivionScrollTargetKind.ExpandedMarkdownBody => -pageScrollOffset,
            OblivionScrollTargetKind.InspectorRawMarkdownSource => -inspectorScrollOffset,
            _ => 0,
        };

        return TranslateVertical(target.Bounds, deltaY);
    }

    private static ScrollbarGeometry GetVisibleScrollbarGeometry(
        OblivionScrollRegionTarget target,
        double pageScrollOffset,
        double inspectorScrollOffset)
    {
        double deltaY = target.Target.Kind switch
        {
            OblivionScrollTargetKind.ExpandedMarkdownBody => -pageScrollOffset,
            OblivionScrollTargetKind.InspectorRawMarkdownSource => -inspectorScrollOffset,
            _ => 0,
        };

        return new ScrollbarGeometry(
            TranslateVertical(target.ScrollbarGeometry.TrackRect, deltaY),
            TranslateVertical(target.ScrollbarGeometry.ThumbRect, deltaY),
            target.ScrollbarGeometry.IsVisible,
            target.ScrollbarGeometry.ScrollOffset,
            target.ScrollbarGeometry.MaxScrollOffset);
    }

    private static Rect TranslateVertical(Rect rect, double deltaY)
    {
        return new Rect(rect.X, rect.Y + deltaY, rect.Width, rect.Height);
    }

    private static bool Contains(Rect rect, double x, double y)
    {
        return x >= rect.X &&
               y >= rect.Y &&
               x < rect.X + rect.Width &&
               y < rect.Y + rect.Height;
    }

    private static ScrollbarInteractionGeometry ToInteractionGeometry(ScrollbarGeometry geometry)
    {
        return new ScrollbarInteractionGeometry(
            geometry.TrackRect,
            geometry.ThumbRect,
            geometry.IsVisible,
            geometry.ScrollOffset,
            geometry.MaxScrollOffset);
    }
}

public sealed record OblivionInspectorSelection(
    IReadOnlyList<OblivionBuiltCard> Cards,
    OblivionBuiltCard? SelectedCard,
    string? SelectedCardId);

internal static class RectExtensions
{
    public static double Area(this Rect rect)
    {
        return rect.Width * rect.Height;
    }
}
