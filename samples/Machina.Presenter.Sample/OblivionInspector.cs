using Machina.Core.Actions;
using Machina.Layout.Geometry;

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
    PresenterPointerCaptureRequest PointerCaptureRequest);

public sealed record OblivionPageInteractionMap(
    string PageId,
    IReadOnlyList<OblivionCardHitTarget> CardTargets,
    IReadOnlyList<OblivionCardBodyHitTarget> BodyTargets,
    IReadOnlyList<OblivionScrollRegionTarget> ScrollRegions)
{
    public UiAction? HitTest(PresenterInputPoint point, double scrollOffset)
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
        PresenterInputEvent inputEvent,
        double pageScrollOffset,
        PresenterScrollbarInteractionState interactionState)
    {
        double contentX = inputEvent.Position.X;
        double contentY = inputEvent.Position.Y + pageScrollOffset;

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

        if (inputEvent.Kind == PresenterInputKind.Wheel)
        {
            bool hoveredScrollableRegion = false;
            foreach (OblivionScrollRegionTarget target in EnumerateScrollableRegionsByPriority(contentX, contentY))
            {
                hoveredScrollableRegion = true;
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
                    PresenterPointerCaptureRequest.None);
            }
        }

        if (inputEvent.Kind == PresenterInputKind.PointerPressed &&
            inputEvent.Button == PresenterInputButton.Primary)
        {
            foreach (OblivionScrollRegionTarget target in EnumerateScrollableRegionsByPriority(contentX, contentY))
            {
                PresenterScrollbarHitPart hitPart = GetScrollbarHitPart(target, contentX, contentY);
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
                        PresenterPointerCaptureRequest.None);
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
                        PresenterPointerCaptureRequest.None);
                }
            }
        }

        return new OblivionPageInteractionRoutingResult(
            null,
            Consumed: false,
            interactionState,
            PresenterPointerCaptureRequest.None);
    }

    public OblivionPageInteractionRoutingResult RouteInput(
        PresenterInputEvent inputEvent,
        double pageScrollOffset)
    {
        return RouteInput(inputEvent, pageScrollOffset, PresenterScrollbarInteractionState.Default);
    }

    private IEnumerable<OblivionScrollRegionTarget> EnumerateScrollableRegionsByPriority(double x, double y)
    {
        return ScrollRegions
            .Where(target => Contains(target.Bounds, x, y))
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

    private static PresenterScrollbarHitPart GetScrollbarHitPart(OblivionScrollRegionTarget target, double x, double y)
    {
        if (target.ScrollbarGeometry.IsVisible && Contains(target.ScrollbarGeometry.ThumbRect, x, y))
        {
            return PresenterScrollbarHitPart.Thumb;
        }

        if (target.ScrollbarGeometry.IsVisible && Contains(target.ScrollbarGeometry.TrackRect, x, y))
        {
            return PresenterScrollbarHitPart.Track;
        }

        if (Contains(target.Bounds, x, y))
        {
            return PresenterScrollbarHitPart.Viewport;
        }

        return PresenterScrollbarHitPart.None;
    }

    private static OblivionPageInteractionRoutingResult ReduceScrollInteraction(
        OblivionScrollRegionTarget target,
        PresenterScrollbarHitPart hitPart,
        PresenterInputEvent inputEvent,
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
            reduced.PointerCaptureRequest);
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
