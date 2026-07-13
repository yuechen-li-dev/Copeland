using Machina.Runtime.Input;

namespace Aurelian.VisibleTriangle;

/// <summary>
/// Host-side normalization kept beside the Silk callback pump. It has no Silk
/// dependency so resize and close ordering can be proven without a window.
/// </summary>
internal static class VisibleTriangleHostInputCollector
{
    public static UiInputBatch Collect(
        ulong batchId,
        bool includeInitialExtent,
        uint width,
        uint height,
        bool closeRequested)
    {
        var events = new List<UiInputEvent>();
        if (includeInitialExtent)
        {
            events.Add(new UiSurfaceResized(new UiSurfaceSize(
                checked((int)width),
                checked((int)height))));
        }

        if (closeRequested)
        {
            events.Add(new UiCloseRequested());
        }

        return new UiInputBatch(batchId, events);
    }
}
