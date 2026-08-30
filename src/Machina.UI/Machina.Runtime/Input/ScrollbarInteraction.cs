using Machina.Layout.Geometry;

namespace Machina.Runtime.Input;

public sealed record ScrollbarInteractionTarget(string Id);

public sealed record ScrollbarInteractionGeometry(
    Rect TrackRect,
    Rect ThumbRect,
    bool IsVisible,
    double ScrollOffset,
    double MaxScrollOffset);

public enum ScrollbarHitPart
{
    None,
    Viewport,
    Track,
    Thumb,
}

public enum PointerCaptureRequest
{
    None,
    Capture,
    Release,
}

public abstract record ScrollbarInteractionState
{
    public sealed record Idle : ScrollbarInteractionState;

    public sealed record ThumbDragging(
        ScrollbarInteractionTarget Target,
        double DragStartPointerY,
        double DragStartScrollOffset,
        ScrollbarInteractionGeometry StartGeometry) : ScrollbarInteractionState;

    public static ScrollbarInteractionState Default { get; } = new Idle();
}

public sealed record ScrollbarInteractionContext(
    ScrollbarInteractionTarget Target,
    ScrollbarInteractionGeometry Geometry,
    double ViewportHeight,
    double WheelMultiplier = 48);

public sealed record ScrollbarInteractionResult(
    ScrollbarInteractionState State,
    double? RequestedScrollOffset,
    PointerCaptureRequest PointerCaptureRequest,
    bool Consumed);

public static class ScrollbarInteraction
{
    public static ScrollbarInteractionResult Reduce(
        ScrollbarInteractionState? currentState,
        ScrollbarInteractionContext context,
        ScrollbarHitPart hitPart,
        UiInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputEvent);

        ScrollbarInteractionState effectiveState = currentState ?? ScrollbarInteractionState.Default;
        return effectiveState switch
        {
            ScrollbarInteractionState.Idle idle => ReduceIdle(idle, context, hitPart, inputEvent),
            ScrollbarInteractionState.ThumbDragging dragging => ReduceDragging(dragging, context, inputEvent),
            _ => new ScrollbarInteractionResult(
                ScrollbarInteractionState.Default,
                null,
                PointerCaptureRequest.None,
                Consumed: false),
        };
    }

    private static ScrollbarInteractionResult ReduceIdle(
        ScrollbarInteractionState.Idle idle,
        ScrollbarInteractionContext context,
        ScrollbarHitPart hitPart,
        UiInputEvent inputEvent)
    {
        if (inputEvent.IsPrimaryPressed() &&
            hitPart == ScrollbarHitPart.Thumb &&
            context.Geometry.IsVisible)
        {
            PointerPoint point = GetPointerPosition(inputEvent);
            return new ScrollbarInteractionResult(
                new ScrollbarInteractionState.ThumbDragging(
                    context.Target,
                    point.Y,
                    context.Geometry.ScrollOffset,
                    context.Geometry),
                null,
                PointerCaptureRequest.Capture,
                Consumed: true);
        }

        if (inputEvent.IsPrimaryPressed() &&
            hitPart == ScrollbarHitPart.Track &&
            context.Geometry.IsVisible)
        {
            double pageDelta = context.ViewportHeight * 0.9;
            double nextOffset = GetPointerPosition(inputEvent).Y < context.Geometry.ThumbRect.Y
                ? context.Geometry.ScrollOffset - pageDelta
                : context.Geometry.ScrollOffset + pageDelta;
            return new ScrollbarInteractionResult(
                idle,
                nextOffset,
                PointerCaptureRequest.None,
                Consumed: true);
        }

        if (inputEvent.IsWheel(out double wheelDeltaY) &&
            hitPart == ScrollbarHitPart.Viewport &&
            context.Geometry.MaxScrollOffset > 0)
        {
            double nextOffset = context.Geometry.ScrollOffset - (wheelDeltaY * context.WheelMultiplier);
            return new ScrollbarInteractionResult(
                idle,
                nextOffset,
                PointerCaptureRequest.None,
                Consumed: true);
        }

        return new ScrollbarInteractionResult(
            idle,
            null,
            PointerCaptureRequest.None,
            Consumed: false);
    }

    private static ScrollbarInteractionResult ReduceDragging(
        ScrollbarInteractionState.ThumbDragging dragging,
        ScrollbarInteractionContext context,
        UiInputEvent inputEvent)
    {
        if (inputEvent.IsPointerMoved())
        {
            return new ScrollbarInteractionResult(
                dragging,
                ComputeDragOffset(dragging, context, GetPointerPosition(inputEvent)),
                PointerCaptureRequest.None,
                Consumed: true);
        }

        if (inputEvent.IsPointerReleased())
        {
            return new ScrollbarInteractionResult(
                ScrollbarInteractionState.Default,
                null,
                PointerCaptureRequest.Release,
                Consumed: true);
        }

        return new ScrollbarInteractionResult(
            dragging,
            null,
            PointerCaptureRequest.None,
            Consumed: true);
    }

    private static double? ComputeDragOffset(
        ScrollbarInteractionState.ThumbDragging dragging,
        ScrollbarInteractionContext context,
        PointerPoint point)
    {
        if (!context.Geometry.IsVisible || context.Target != dragging.Target)
        {
            return null;
        }

        double thumbTravel = dragging.StartGeometry.TrackRect.Height - dragging.StartGeometry.ThumbRect.Height;
        if (thumbTravel <= 0 || dragging.StartGeometry.MaxScrollOffset <= 0)
        {
            return dragging.DragStartScrollOffset;
        }

        double deltaY = point.Y - dragging.DragStartPointerY;
        double scrollDelta = deltaY * (dragging.StartGeometry.MaxScrollOffset / thumbTravel);
        return dragging.DragStartScrollOffset + scrollDelta;
    }

    private static PointerPoint GetPointerPosition(UiInputEvent inputEvent)
    {
        return inputEvent.TryGetPointerPosition(out PointerPoint point)
            ? point
            : throw new InvalidOperationException("Scrollbar routing requires pointer input.");
    }
}
