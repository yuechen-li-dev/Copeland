using Aurelian.Rendering.Contracts.Resolved2D;

namespace Aurelian.Rendering.Raster;

/// <summary>
/// Synchronous deterministic realization of a resolved 2D plan.
/// </summary>
public sealed class AurelianCpuRasterRenderer
{
    public RasterFrame Render(Resolved2DPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var buffer = new RasterBuffer(plan.Viewport.Width, plan.Viewport.Height);
        var clips = new Stack<PixelBounds>();
        PixelBounds currentClip = PixelBounds.FromSurface(buffer.Width, buffer.Height);

        foreach (Resolved2DOperation operation in plan.Operations)
        {
            switch (operation)
            {
                case FillRectangleOperation fill:
                    buffer.FillRectangle(fill.Rectangle, fill.Color, currentClip);
                    break;
                case StrokeRectangleOperation stroke:
                    buffer.StrokeRectangle(stroke.Rectangle, stroke.Color, stroke.Thickness, currentClip);
                    break;
                case PositionedTextOperation text:
                    DeterministicBitmapTextRenderer.Draw(buffer, text, currentClip);
                    break;
                case PushRectangularClipOperation push:
                    clips.Push(currentClip);
                    currentClip = PixelBounds.Intersect(currentClip, PixelBounds.FromRectangle(push.Rectangle));
                    break;
                case PopClipOperation:
                    currentClip = clips.Pop();
                    break;
                default:
                    throw new InvalidOperationException($"Resolved 2D operation '{operation.GetType().FullName}' is not supported.");
            }
        }

        return new RasterFrame(buffer.Complete());
    }
}
