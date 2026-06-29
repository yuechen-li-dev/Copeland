using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public abstract record PresenterScrollbarInteractionState
{
    public sealed record Idle : PresenterScrollbarInteractionState;

    public sealed record ThumbDragging(
        string PageId,
        float DragStartPointerY,
        float DragStartScrollOffset,
        ScrollbarGeometry StartGeometry) : PresenterScrollbarInteractionState;

    public static PresenterScrollbarInteractionState Default { get; } = new Idle();
}

public enum PresenterPointerCaptureRequest
{
    None,
    Capture,
    Release,
}

public sealed record PresenterScrollbarInteractionContext(
    string PageId,
    ScrollbarGeometry ScrollbarGeometry,
    double ViewportHeight);

public sealed record PresenterScrollbarInteractionResult(
    PresenterScrollbarInteractionState State,
    UiActionId? ActionId,
    PresenterPointerCaptureRequest PointerCaptureRequest,
    bool SuppressFurtherRouting);

public static class PresenterScrollbarInteractionStateMachine
{
    public static PresenterScrollbarInteractionResult Reduce(
        PresenterScrollbarInteractionState? currentState,
        PresenterScrollbarInteractionContext context,
        PresenterNavigationHitTarget hitTarget,
        PresenterInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hitTarget);
        ArgumentNullException.ThrowIfNull(inputEvent);

        PresenterScrollbarInteractionState effectiveState = currentState ?? PresenterScrollbarInteractionState.Default;

        return effectiveState switch
        {
            PresenterScrollbarInteractionState.Idle idle => ReduceIdle(idle, context, hitTarget, inputEvent),
            PresenterScrollbarInteractionState.ThumbDragging dragging => ReduceThumbDragging(dragging, context, inputEvent),
            _ => new PresenterScrollbarInteractionResult(
                PresenterScrollbarInteractionState.Default,
                null,
                PresenterPointerCaptureRequest.None,
                SuppressFurtherRouting: false),
        };
    }

    private static PresenterScrollbarInteractionResult ReduceIdle(
        PresenterScrollbarInteractionState.Idle idle,
        PresenterScrollbarInteractionContext context,
        PresenterNavigationHitTarget hitTarget,
        PresenterInputEvent inputEvent)
    {
        if (inputEvent.Kind == PresenterInputKind.PointerPressed &&
            inputEvent.Button == PresenterInputButton.Primary &&
            hitTarget.Kind == PresenterNavigationHitKind.ScrollbarThumb &&
            context.ScrollbarGeometry.IsVisible)
        {
            return new PresenterScrollbarInteractionResult(
                new PresenterScrollbarInteractionState.ThumbDragging(
                    context.PageId,
                    inputEvent.Position.Y,
                    (float)context.ScrollbarGeometry.ScrollOffset,
                    context.ScrollbarGeometry),
                null,
                PresenterPointerCaptureRequest.Capture,
                SuppressFurtherRouting: true);
        }

        if (inputEvent.Kind == PresenterInputKind.PointerPressed &&
            inputEvent.Button == PresenterInputButton.Primary &&
            hitTarget.Kind == PresenterNavigationHitKind.ScrollbarTrack &&
            context.ScrollbarGeometry.IsVisible)
        {
            double pageDelta = context.ViewportHeight * 0.9;
            double nextOffset = inputEvent.Position.Y < context.ScrollbarGeometry.ThumbRect.Y
                ? context.ScrollbarGeometry.ScrollOffset - pageDelta
                : context.ScrollbarGeometry.ScrollOffset + pageDelta;

            return new PresenterScrollbarInteractionResult(
                idle,
                PresenterNavigationActions.SetScrollOffset(context.PageId, nextOffset),
                PresenterPointerCaptureRequest.None,
                SuppressFurtherRouting: true);
        }

        if (inputEvent.Kind == PresenterInputKind.Wheel &&
            hitTarget.Kind == PresenterNavigationHitKind.ContentViewport &&
            context.ScrollbarGeometry.MaxScrollOffset > 0)
        {
            double nextOffset = context.ScrollbarGeometry.ScrollOffset - (inputEvent.WheelDeltaY * PresenterNavigationInputRouter.ScrollWheelMultiplier);

            return new PresenterScrollbarInteractionResult(
                idle,
                PresenterNavigationActions.SetScrollOffset(context.PageId, nextOffset),
                PresenterPointerCaptureRequest.None,
                SuppressFurtherRouting: true);
        }

        return new PresenterScrollbarInteractionResult(
            idle,
            null,
            PresenterPointerCaptureRequest.None,
            SuppressFurtherRouting: false);
    }

    private static PresenterScrollbarInteractionResult ReduceThumbDragging(
        PresenterScrollbarInteractionState.ThumbDragging dragging,
        PresenterScrollbarInteractionContext context,
        PresenterInputEvent inputEvent)
    {
        if (inputEvent.Kind == PresenterInputKind.PointerMoved)
        {
            UiActionId? actionId = BuildThumbDragAction(dragging, context, inputEvent.Position);
            return new PresenterScrollbarInteractionResult(
                dragging,
                actionId,
                PresenterPointerCaptureRequest.None,
                SuppressFurtherRouting: true);
        }

        if (inputEvent.Kind == PresenterInputKind.PointerReleased)
        {
            return new PresenterScrollbarInteractionResult(
                PresenterScrollbarInteractionState.Default,
                null,
                PresenterPointerCaptureRequest.Release,
                SuppressFurtherRouting: true);
        }

        return new PresenterScrollbarInteractionResult(
            dragging,
            null,
            PresenterPointerCaptureRequest.None,
            SuppressFurtherRouting: true);
    }

    private static UiActionId? BuildThumbDragAction(
        PresenterScrollbarInteractionState.ThumbDragging dragging,
        PresenterScrollbarInteractionContext context,
        PresenterInputPoint position)
    {
        if (!context.ScrollbarGeometry.IsVisible ||
            !string.Equals(context.PageId, dragging.PageId, StringComparison.Ordinal))
        {
            return null;
        }

        double thumbTravel = dragging.StartGeometry.TrackRect.Height - dragging.StartGeometry.ThumbRect.Height;
        if (thumbTravel <= 0 || dragging.StartGeometry.MaxScrollOffset <= 0)
        {
            return PresenterNavigationActions.SetScrollOffset(dragging.PageId, dragging.DragStartScrollOffset);
        }

        double deltaY = position.Y - dragging.DragStartPointerY;
        double scrollDelta = deltaY * (dragging.StartGeometry.MaxScrollOffset / thumbTravel);
        double nextOffset = dragging.DragStartScrollOffset + scrollDelta;
        return PresenterNavigationActions.SetScrollOffset(dragging.PageId, nextOffset);
    }
}
