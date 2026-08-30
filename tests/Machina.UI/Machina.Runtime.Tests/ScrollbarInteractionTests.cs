using Machina.Layout.Geometry;
using Machina.Runtime.Input;
using Xunit;

namespace Machina.Runtime.Tests;

public sealed class ScrollbarInteractionTests
{
    [Fact]
    public void Wheel_requests_offset_without_assigning_product_semantics()
    {
        ScrollbarInteractionResult result = ScrollbarInteraction.Reduce(
            ScrollbarInteractionState.Default,
            CreateContext(),
            ScrollbarHitPart.Viewport,
            new UiPointerWheel(
                new PointerPoint(10, 20),
                DeltaX: 0,
                DeltaY: -1,
                UiModifiers.None));

        Assert.True(result.Consumed);
        Assert.Equal(148, result.RequestedScrollOffset);
        Assert.Equal(PointerCaptureRequest.None, result.PointerCaptureRequest);
    }

    [Fact]
    public void Thumb_drag_has_explicit_capture_and_release_lifecycle()
    {
        ScrollbarInteractionContext context = CreateContext();
        ScrollbarInteractionResult pressed = ScrollbarInteraction.Reduce(
            ScrollbarInteractionState.Default,
            context,
            ScrollbarHitPart.Thumb,
            new UiPointerButtonChanged(
                new PointerPoint(10, 20),
                UiPointerButton.Primary,
                IsPressed: true,
                UiModifiers.None));

        Assert.IsType<ScrollbarInteractionState.ThumbDragging>(pressed.State);
        Assert.Equal(PointerCaptureRequest.Capture, pressed.PointerCaptureRequest);

        ScrollbarInteractionResult moved = ScrollbarInteraction.Reduce(
            pressed.State,
            context,
            ScrollbarHitPart.None,
            new UiPointerMoved(
                new PointerPoint(10, 30),
                PreviousPosition: new PointerPoint(10, 20),
                UiModifiers.None));
        Assert.True(moved.RequestedScrollOffset > 100);

        ScrollbarInteractionResult released = ScrollbarInteraction.Reduce(
            moved.State,
            context,
            ScrollbarHitPart.None,
            new UiPointerButtonChanged(
                new PointerPoint(10, 30),
                UiPointerButton.Primary,
                IsPressed: false,
                UiModifiers.None));
        Assert.IsType<ScrollbarInteractionState.Idle>(released.State);
        Assert.Equal(PointerCaptureRequest.Release, released.PointerCaptureRequest);
    }

    private static ScrollbarInteractionContext CreateContext()
    {
        return new ScrollbarInteractionContext(
            new ScrollbarInteractionTarget("generic-region"),
            new ScrollbarInteractionGeometry(
                new Rect(0, 0, 12, 200),
                new Rect(0, 10, 12, 40),
                IsVisible: true,
                ScrollOffset: 100,
                MaxScrollOffset: 400),
            ViewportHeight: 200);
    }
}
