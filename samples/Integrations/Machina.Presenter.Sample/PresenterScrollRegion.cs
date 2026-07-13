using Machina.Layout.Geometry;

namespace Machina.Presenter.Sample;

public sealed record ScrollbarGeometry(
    Rect TrackRect,
    Rect ThumbRect,
    bool IsVisible,
    double ScrollOffset,
    double MaxScrollOffset);

public static class PresenterScrollRegion
{
    private const double MinimumThumbHeight = 32;

    public static double ComputeMaxScrollOffset(double contentHeight, double viewportHeight)
    {
        if (contentHeight <= viewportHeight)
        {
            return 0;
        }

        return contentHeight - viewportHeight;
    }

    public static double ClampScrollOffset(double contentHeight, double viewportHeight, double requestedOffset)
    {
        double maxScrollOffset = ComputeMaxScrollOffset(contentHeight, viewportHeight);
        return Math.Clamp(requestedOffset, 0, maxScrollOffset);
    }

    public static ScrollbarGeometry ComputeScrollbarGeometry(
        Rect trackRect,
        double contentHeight,
        double viewportHeight,
        double requestedOffset)
    {
        double maxScrollOffset = ComputeMaxScrollOffset(contentHeight, viewportHeight);
        double clampedOffset = ClampScrollOffset(contentHeight, viewportHeight, requestedOffset);

        if (maxScrollOffset <= 0 || trackRect.Height <= 0 || trackRect.Width <= 0)
        {
            return new ScrollbarGeometry(
                TrackRect: trackRect,
                ThumbRect: new Rect(trackRect.X, trackRect.Y, 0, 0),
                IsVisible: false,
                ScrollOffset: 0,
                MaxScrollOffset: 0);
        }

        double viewportRatio = viewportHeight / contentHeight;
        double thumbHeight = Math.Max(MinimumThumbHeight, Math.Floor(trackRect.Height * viewportRatio));
        thumbHeight = Math.Min(thumbHeight, trackRect.Height);

        double thumbTravel = Math.Max(0, trackRect.Height - thumbHeight);
        double thumbProgress = maxScrollOffset <= 0 ? 0 : clampedOffset / maxScrollOffset;
        double unclampedThumbTop = trackRect.Y + Math.Floor(thumbTravel * thumbProgress);
        double thumbTop = Math.Clamp(
            unclampedThumbTop,
            trackRect.Y,
            trackRect.Y + trackRect.Height - thumbHeight);

        return new ScrollbarGeometry(
            TrackRect: trackRect,
            ThumbRect: new Rect(trackRect.X, thumbTop, trackRect.Width, thumbHeight),
            IsVisible: true,
            ScrollOffset: clampedOffset,
            MaxScrollOffset: maxScrollOffset);
    }
}
