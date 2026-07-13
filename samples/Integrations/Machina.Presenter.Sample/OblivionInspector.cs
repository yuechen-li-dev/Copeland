using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

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
    PresenterScrollbarTarget Target,
    Rect Bounds,
    ScrollbarGeometry ScrollbarGeometry,
    double ContentHeight);

public sealed record OblivionPageInteractionRoutingResult(
    UiAction? Action,
    bool Consumed,
    PresenterScrollbarInteractionState InteractionState,
    PresenterPointerCaptureRequest PointerCaptureRequest,
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
        PresenterScrollbarInteractionState interactionState)
    {
        if (!inputEvent.TryGetPointerPosition(out PointerPoint pointerPosition))
        {
            return new OblivionPageInteractionRoutingResult(
                null,
                Consumed: false,
                interactionState,
                PresenterPointerCaptureRequest.None,
                null);
        }

        double contentX = pointerPosition.X;
        double contentY = pointerPosition.Y + pageScrollOffset;

        if (interactionState is PresenterScrollbarInteractionState.ThumbDragging dragging)
        {
            OblivionScrollRegionTarget? dragRegion = ScrollRegions.FirstOrDefault(region => Equals(region.Target, dragging.Target));
            if (dragRegion is not null)
            {
                return ReduceScrollInteraction(
                    dragRegion,
                    PresenterScrollbarHitPart.None,
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
                    PresenterScrollbarHitPart.Viewport,
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
                    PresenterPointerCaptureRequest.None,
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
                PresenterScrollbarHitPart hitPart = GetScrollbarHitPart(target, contentX, contentY, pageScrollOffset);
                if (hitPart == PresenterScrollbarHitPart.None)
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
                        PresenterPointerCaptureRequest.None,
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
                        PresenterPointerCaptureRequest.None,
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
            PresenterPointerCaptureRequest.None,
            null);
    }

    public OblivionPageInteractionRoutingResult RouteInput(
        UiInputEvent inputEvent,
        double pageScrollOffset)
    {
        return RouteInput(inputEvent, pageScrollOffset, PresenterScrollbarInteractionState.Default);
    }

    private IEnumerable<OblivionScrollRegionTarget> EnumerateScrollableRegionsByPriority(double x, double y, double pageScrollOffset)
    {
        double inspectorScrollOffset = ScrollRegions
            .FirstOrDefault(region => region.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorPane)?
            .ScrollbarGeometry.ScrollOffset
            ?? 0;

        return ScrollRegions
            .Where(target => ContainsInteractiveBounds(target, x, y, pageScrollOffset, inspectorScrollOffset))
            .OrderByDescending(target => GetPriority(target.Target.Kind))
            .ThenBy(target => target.Bounds.Area(), Comparer<double>.Create((left, right) => left.CompareTo(right)));
    }

    private static int GetPriority(PresenterScrollbarTargetKind kind)
    {
        return kind switch
        {
            PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource => 4,
            PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody => 3,
            PresenterScrollbarTargetKind.OblivionInspectorPane => 2,
            PresenterScrollbarTargetKind.OblivionMainCardStack => 1,
            _ => 0,
        };
    }

    private PresenterScrollbarHitPart GetScrollbarHitPart(OblivionScrollRegionTarget target, double x, double y, double pageScrollOffset)
    {
        double inspectorScrollOffset = ScrollRegions
            .FirstOrDefault(region => region.Target.Kind == PresenterScrollbarTargetKind.OblivionInspectorPane)?
            .ScrollbarGeometry.ScrollOffset
            ?? 0;
        ScrollbarGeometry visibleGeometry = GetVisibleScrollbarGeometry(target, pageScrollOffset, inspectorScrollOffset);

        if (target.ScrollbarGeometry.IsVisible &&
            (Contains(target.ScrollbarGeometry.ThumbRect, x, y) || Contains(visibleGeometry.ThumbRect, x, y)))
        {
            return PresenterScrollbarHitPart.Thumb;
        }

        if (target.ScrollbarGeometry.IsVisible &&
            (Contains(target.ScrollbarGeometry.TrackRect, x, y) || Contains(visibleGeometry.TrackRect, x, y)))
        {
            return PresenterScrollbarHitPart.Track;
        }

        if (ContainsInteractiveBounds(target, x, y, pageScrollOffset, inspectorScrollOffset))
        {
            return PresenterScrollbarHitPart.Viewport;
        }

        return PresenterScrollbarHitPart.None;
    }

    private static OblivionPageInteractionRoutingResult ReduceScrollInteraction(
        OblivionScrollRegionTarget target,
        PresenterScrollbarHitPart hitPart,
        UiInputEvent inputEvent,
        PresenterScrollbarInteractionState interactionState)
    {
        PresenterScrollbarInteractionResult reduced = PresenterScrollbarInteractionStateMachine.Reduce(
            interactionState,
            new PresenterScrollbarInteractionContext(
                target.Target,
                target.ScrollbarGeometry,
                target.Bounds.Height),
            hitPart,
            inputEvent);

        return new OblivionPageInteractionRoutingResult(
            reduced.ActionId is null ? null : new UiAction(reduced.ActionId.Value),
            reduced.SuppressFurtherRouting,
            reduced.State,
            reduced.PointerCaptureRequest,
            new OblivionInteractionHitResult(
                RegionKind: GetScrollRegionKind(target.Target.Kind),
                RegionId: BuildScrollRegionId(target.Target),
                CardId: target.Target.CardId,
                ScrollRegionId: BuildScrollRegionId(target.Target),
                LocalPoint: inputEvent.TryGetPointerPosition(out PointerPoint position) ? position : default));
    }

    private static string GetScrollRegionKind(PresenterScrollbarTargetKind kind)
    {
        return kind switch
        {
            PresenterScrollbarTargetKind.OblivionMainCardStack => "oblivion-main-card-stack",
            PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody => "oblivion-expanded-markdown-body",
            PresenterScrollbarTargetKind.OblivionInspectorPane => "oblivion-inspector-pane",
            PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource => "oblivion-inspector-raw-markdown-source",
            PresenterScrollbarTargetKind.Page => "page",
            _ => "unknown",
        };
    }

    private static string BuildScrollRegionId(PresenterScrollbarTarget target)
    {
        return target.Kind switch
        {
            PresenterScrollbarTargetKind.OblivionMainCardStack => $"{target.PageId}.main-stack",
            PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody => $"{target.PageId}.{target.CardId}.expanded-body",
            PresenterScrollbarTargetKind.OblivionInspectorPane => $"{target.PageId}.inspector-pane",
            PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource => $"{target.PageId}.{target.CardId}.raw-source",
            PresenterScrollbarTargetKind.Page => $"{target.PageId}.page",
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
            PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody => -pageScrollOffset,
            PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource => -inspectorScrollOffset,
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
            PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody => -pageScrollOffset,
            PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource => -inspectorScrollOffset,
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
