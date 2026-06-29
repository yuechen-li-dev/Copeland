using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Surface;

namespace Machina.Presenter.Sample;

public static class PresenterNavigationFrameComposer
{
    private static readonly Rgba32 ScrollbarThumbColor = Rgba32.FromRgba(0x64748BFF);

    public static RasterFrame Compose(
        RasterFrame shellFrame,
        RasterFrame pageFrame,
        Rect viewportRect,
        ScrollbarGeometry scrollbarGeometry)
    {
        ArgumentNullException.ThrowIfNull(shellFrame);
        ArgumentNullException.ThrowIfNull(pageFrame);

        RasterSurface composedSurface = CloneSurface(shellFrame.Surface);
        BlitPageContent(pageFrame.Surface, composedSurface, viewportRect, scrollbarGeometry.ScrollOffset);

        if (scrollbarGeometry.IsVisible)
        {
            FillRect(composedSurface, scrollbarGeometry.ThumbRect, ScrollbarThumbColor);
        }

        return new RasterFrame(shellFrame.Width, shellFrame.Height, composedSurface);
    }

    public static void BlitPageContent(
        RasterSurface source,
        RasterSurface destination,
        Rect viewportRect,
        double scrollOffset)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        BlitRect blit = ComputeBlitRect(source, destination, viewportRect, scrollOffset);
        if (blit.Width <= 0 || blit.Height <= 0)
        {
            return;
        }

        for (int row = 0; row < blit.Height; row++)
        {
            int sourceIndex = ((blit.SourceY + row) * source.Width) + blit.SourceX;
            int destinationIndex = ((blit.DestinationY + row) * destination.Width) + blit.DestinationX;
            for (int column = 0; column < blit.Width; column++)
            {
                Rgba32 pixel = source.Pixels[sourceIndex + column];
                if (pixel.A == 0)
                {
                    continue;
                }

                destination.Pixels[destinationIndex + column] = pixel;
            }
        }
    }

    public static BlitRect ComputeBlitRect(
        RasterSurface source,
        RasterSurface destination,
        Rect viewportRect,
        double scrollOffset)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        int sourceX = 0;
        int sourceY = Math.Clamp((int)Math.Floor(scrollOffset), 0, Math.Max(0, source.Height - 1));
        int destinationX = (int)Math.Floor(viewportRect.X);
        int destinationY = (int)Math.Floor(viewportRect.Y);
        int requestedWidth = (int)Math.Floor(viewportRect.Width);
        int requestedHeight = (int)Math.Floor(viewportRect.Height);

        int visibleLeftTrim = Math.Max(0, -destinationX);
        int visibleTopTrim = Math.Max(0, -destinationY);

        sourceX += visibleLeftTrim;
        sourceY += visibleTopTrim;
        destinationX += visibleLeftTrim;
        destinationY += visibleTopTrim;

        int width = Math.Min(requestedWidth - visibleLeftTrim, source.Width - sourceX);
        width = Math.Min(width, destination.Width - destinationX);

        int height = Math.Min(requestedHeight - visibleTopTrim, source.Height - sourceY);
        height = Math.Min(height, destination.Height - destinationY);

        return new BlitRect(
            SourceX: sourceX,
            SourceY: sourceY,
            DestinationX: destinationX,
            DestinationY: destinationY,
            Width: Math.Max(0, width),
            Height: Math.Max(0, height));
    }

    private static RasterSurface CloneSurface(RasterSurface source)
    {
        var clone = new RasterSurface(source.Width, source.Height);
        Array.Copy(source.Pixels, clone.Pixels, source.Pixels.Length);
        return clone;
    }

    private static void FillRect(RasterSurface surface, Rect rect, Rgba32 color)
    {
        int left = Math.Max(0, (int)Math.Floor(rect.X));
        int top = Math.Max(0, (int)Math.Floor(rect.Y));
        int right = Math.Min(surface.Width, (int)Math.Ceiling(rect.X + rect.Width));
        int bottom = Math.Min(surface.Height, (int)Math.Ceiling(rect.Y + rect.Height));

        if (right <= left || bottom <= top)
        {
            return;
        }

        int rowWidth = right - left;
        Rgba32[] rowPixels = new Rgba32[rowWidth];
        Array.Fill(rowPixels, color);

        for (int y = top; y < bottom; y++)
        {
            Array.Copy(rowPixels, 0, surface.Pixels, (y * surface.Width) + left, rowWidth);
        }
    }
}

public sealed record BlitRect(
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY,
    int Width,
    int Height);
