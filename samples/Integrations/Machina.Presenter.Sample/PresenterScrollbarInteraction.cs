using Machina.Core.Actions;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

public sealed record PresenterScrollbarTarget(string PageId)
{
    public ScrollbarInteractionTarget ToRuntimeTarget()
    {
        return new ScrollbarInteractionTarget($"presenter-page|{PageId}");
    }
}

public sealed record PresenterScrollbarInteractionContext(
    PresenterScrollbarTarget Target,
    ScrollbarGeometry ScrollbarGeometry,
    double ViewportHeight);

public sealed record PresenterScrollbarInteractionResult(
    ScrollbarInteractionState State,
    UiActionId? ActionId,
    PointerCaptureRequest PointerCaptureRequest,
    bool SuppressFurtherRouting);

public static class PresenterScrollbarInteraction
{
    public static PresenterScrollbarInteractionResult Reduce(
        ScrollbarInteractionState? currentState,
        PresenterScrollbarInteractionContext context,
        ScrollbarHitPart hitPart,
        UiInputEvent inputEvent)
    {
        ScrollbarInteractionResult result = ScrollbarInteraction.Reduce(
            currentState,
            new ScrollbarInteractionContext(
                context.Target.ToRuntimeTarget(),
                new ScrollbarInteractionGeometry(
                    context.ScrollbarGeometry.TrackRect,
                    context.ScrollbarGeometry.ThumbRect,
                    context.ScrollbarGeometry.IsVisible,
                    context.ScrollbarGeometry.ScrollOffset,
                    context.ScrollbarGeometry.MaxScrollOffset),
                context.ViewportHeight),
            hitPart,
            inputEvent);

        UiActionId? actionId = result.RequestedScrollOffset is double offset
            ? PresenterNavigationActions.SetScrollOffset(context.Target.PageId, offset)
            : null;

        return new PresenterScrollbarInteractionResult(
            result.State,
            actionId,
            result.PointerCaptureRequest,
            result.Consumed);
    }
}
