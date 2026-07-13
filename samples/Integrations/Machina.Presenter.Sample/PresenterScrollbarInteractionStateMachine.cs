using Machina.Core.Actions;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

public enum PresenterScrollbarTargetKind
{
    Page,
    OblivionMainCardStack,
    OblivionExpandedMarkdownBody,
    OblivionInspectorPane,
    OblivionInspectorRawMarkdownSource,
}

public sealed record PresenterScrollbarTarget(
    PresenterScrollbarTargetKind Kind,
    string PageId,
    string? CardId = null);

public enum PresenterScrollbarHitPart
{
    None,
    Viewport,
    Track,
    Thumb,
}

public abstract record PresenterScrollbarInteractionState
{
    public sealed record Idle : PresenterScrollbarInteractionState;

    public sealed record ThumbDragging(
        PresenterScrollbarTarget Target,
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
    PresenterScrollbarTarget Target,
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
        PresenterScrollbarHitPart hitPart,
        UiInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputEvent);

        PresenterScrollbarInteractionState effectiveState = currentState ?? PresenterScrollbarInteractionState.Default;

        return effectiveState switch
        {
            PresenterScrollbarInteractionState.Idle idle => ReduceIdle(idle, context, hitPart, inputEvent),
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
        PresenterScrollbarHitPart hitPart,
        UiInputEvent inputEvent)
    {
        if (inputEvent.IsPrimaryPressed() &&
            hitPart == PresenterScrollbarHitPart.Thumb &&
            context.ScrollbarGeometry.IsVisible)
        {
            return new PresenterScrollbarInteractionResult(
                new PresenterScrollbarInteractionState.ThumbDragging(
                    context.Target,
                    checked((float)GetPointerPosition(inputEvent).Y),
                    (float)context.ScrollbarGeometry.ScrollOffset,
                    context.ScrollbarGeometry),
                null,
                PresenterPointerCaptureRequest.Capture,
                SuppressFurtherRouting: true);
        }

        if (inputEvent.IsPrimaryPressed() &&
            hitPart == PresenterScrollbarHitPart.Track &&
            context.ScrollbarGeometry.IsVisible)
        {
            double pageDelta = context.ViewportHeight * 0.9;
            double nextOffset = GetPointerPosition(inputEvent).Y < context.ScrollbarGeometry.ThumbRect.Y
                ? context.ScrollbarGeometry.ScrollOffset - pageDelta
                : context.ScrollbarGeometry.ScrollOffset + pageDelta;

            return new PresenterScrollbarInteractionResult(
                idle,
                BuildSetScrollAction(context.Target, nextOffset),
                PresenterPointerCaptureRequest.None,
                SuppressFurtherRouting: true);
        }

        if (inputEvent.IsWheel(out double wheelDeltaY) &&
            hitPart == PresenterScrollbarHitPart.Viewport &&
            context.ScrollbarGeometry.MaxScrollOffset > 0)
        {
            double nextOffset = context.ScrollbarGeometry.ScrollOffset - (wheelDeltaY * PresenterNavigationInputRouter.ScrollWheelMultiplier);

            return new PresenterScrollbarInteractionResult(
                idle,
                BuildSetScrollAction(context.Target, nextOffset),
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
        UiInputEvent inputEvent)
    {
        if (inputEvent.IsPointerMoved())
        {
            UiActionId? actionId = BuildThumbDragAction(dragging, context, GetPointerPosition(inputEvent));
            return new PresenterScrollbarInteractionResult(
                dragging,
                actionId,
                PresenterPointerCaptureRequest.None,
                SuppressFurtherRouting: true);
        }

        if (inputEvent.IsPointerReleased())
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
        PointerPoint position)
    {
        if (!context.ScrollbarGeometry.IsVisible ||
            !Equals(context.Target, dragging.Target))
        {
            return null;
        }

        double thumbTravel = dragging.StartGeometry.TrackRect.Height - dragging.StartGeometry.ThumbRect.Height;
        if (thumbTravel <= 0 || dragging.StartGeometry.MaxScrollOffset <= 0)
        {
            return BuildSetScrollAction(dragging.Target, dragging.DragStartScrollOffset);
        }

        double deltaY = position.Y - dragging.DragStartPointerY;
        double scrollDelta = deltaY * (dragging.StartGeometry.MaxScrollOffset / thumbTravel);
        double nextOffset = dragging.DragStartScrollOffset + scrollDelta;
        return BuildSetScrollAction(dragging.Target, nextOffset);
    }

    private static UiActionId BuildSetScrollAction(PresenterScrollbarTarget target, double nextOffset)
    {
        return target.Kind switch
        {
            PresenterScrollbarTargetKind.Page =>
                PresenterNavigationActions.SetScrollOffset(target.PageId, nextOffset),
            PresenterScrollbarTargetKind.OblivionMainCardStack =>
                PresenterNavigationActions.SetOblivionMainCardStackScrollOffset(target.PageId, nextOffset),
            PresenterScrollbarTargetKind.OblivionExpandedMarkdownBody =>
                PresenterNavigationActions.SetOblivionCardBodyScrollOffset(
                    target.PageId,
                    target.CardId ?? throw new InvalidOperationException("Expanded Markdown body scroll target requires a card id."),
                    nextOffset),
            PresenterScrollbarTargetKind.OblivionInspectorPane =>
                PresenterNavigationActions.SetOblivionInspectorScrollOffset(target.PageId, nextOffset),
            PresenterScrollbarTargetKind.OblivionInspectorRawMarkdownSource =>
                PresenterNavigationActions.SetOblivionRawMarkdownSourceScrollOffset(
                    target.PageId,
                    target.CardId ?? throw new InvalidOperationException("Raw Markdown source scroll target requires a card id."),
                    nextOffset),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported scrollbar target."),
        };
    }

    private static PointerPoint GetPointerPosition(UiInputEvent inputEvent)
    {
        return inputEvent.TryGetPointerPosition(out PointerPoint position)
            ? position
            : throw new InvalidOperationException("Scrollbar routing requires pointer input.");
    }
}
