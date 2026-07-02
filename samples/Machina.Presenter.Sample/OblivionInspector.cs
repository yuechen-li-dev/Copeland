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

public sealed record OblivionPageInteractionRoutingResult(
    UiAction? Action,
    bool Consumed);

public sealed record OblivionPageInteractionMap(
    string PageId,
    IReadOnlyList<OblivionCardHitTarget> CardTargets,
    IReadOnlyList<OblivionCardBodyHitTarget> BodyTargets)
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

    public OblivionPageInteractionRoutingResult RouteInput(PresenterInputEvent inputEvent, double pageScrollOffset)
    {
        double contentX = inputEvent.Position.X;
        double contentY = inputEvent.Position.Y + pageScrollOffset;

        if (inputEvent.Kind == PresenterInputKind.Wheel)
        {
            foreach (OblivionCardBodyHitTarget target in BodyTargets)
            {
                if (!Contains(target.Bounds, contentX, contentY))
                {
                    continue;
                }

                double nextOffset = target.ScrollbarGeometry.ScrollOffset - (inputEvent.WheelDeltaY * PresenterNavigationInputRouter.ScrollWheelMultiplier);
                double clamped = PresenterScrollRegion.ClampScrollOffset(target.ContentHeight, target.Bounds.Height, nextOffset);
                if (Math.Abs(clamped - target.ScrollbarGeometry.ScrollOffset) > 0.001)
                {
                    return new OblivionPageInteractionRoutingResult(
                        new UiAction(PresenterNavigationActions.SetOblivionCardBodyScrollOffset(target.PageId, target.CardId, clamped)),
                        Consumed: true);
                }

                return new OblivionPageInteractionRoutingResult(null, Consumed: false);
            }
        }

        if (inputEvent.Kind == PresenterInputKind.PointerPressed &&
            inputEvent.Button == PresenterInputButton.Primary)
        {
            foreach (OblivionCardBodyHitTarget target in BodyTargets)
            {
                if (Contains(target.Bounds, contentX, contentY))
                {
                    return new OblivionPageInteractionRoutingResult(new UiAction(target.SelectActionId), Consumed: true);
                }
            }

            foreach (OblivionCardHitTarget target in CardTargets)
            {
                if (Contains(target.HeaderBounds, contentX, contentY) ||
                    Contains(target.Bounds, contentX, contentY))
                {
                    return new OblivionPageInteractionRoutingResult(new UiAction(target.ActionId), Consumed: true);
                }
            }
        }

        return new OblivionPageInteractionRoutingResult(null, Consumed: false);
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
