using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Rasterization;

public static class Rasterizer
{
    public static void Clear(RasterSurface surface, Rgba32 color)
    {
        ArgumentNullException.ThrowIfNull(surface);

        for (var i = 0; i < surface.Pixels.Length; i++)
        {
            surface.Pixels[i] = color;
        }
    }

    public static void FillRect(RasterSurface surface, Rect rect, Rgba32 color)
    {
        FillRect(surface, rect, color, clip: null);
    }

    public static void FillRect(RasterSurface surface, Rect rect, Rgba32 color, Rect? clip)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!IsFinite(rect.X) || !IsFinite(rect.Y) || !IsFinite(rect.Width) || !IsFinite(rect.Height))
        {
            throw new ArgumentException("Rect coordinates must be finite numbers.", nameof(rect));
        }

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var left = (int)Math.Floor(rect.X);
        var top = (int)Math.Floor(rect.Y);
        var right = (int)Math.Ceiling(rect.X + rect.Width);
        var bottom = (int)Math.Ceiling(rect.Y + rect.Height);

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(surface.Width, right);
        var clippedBottom = Math.Min(surface.Height, bottom);

        if (clip is not null)
        {
            ValidateFiniteRect(clip.Value, "Clip coordinates must be finite numbers.", nameof(clip));

            var clipRect = clip.Value;
            if (clipRect.Width <= 0 || clipRect.Height <= 0)
            {
                return;
            }

            var clipLeft = (int)Math.Floor(clipRect.X);
            var clipTop = (int)Math.Floor(clipRect.Y);
            var clipRight = (int)Math.Ceiling(clipRect.X + clipRect.Width);
            var clipBottom = (int)Math.Ceiling(clipRect.Y + clipRect.Height);

            clippedLeft = Math.Max(clippedLeft, clipLeft);
            clippedTop = Math.Max(clippedTop, clipTop);
            clippedRight = Math.Min(clippedRight, clipRight);
            clippedBottom = Math.Min(clippedBottom, clipBottom);
        }

        if (clippedLeft >= clippedRight || clippedTop >= clippedBottom)
        {
            return;
        }

        for (var y = clippedTop; y < clippedBottom; y++)
        {
            for (var x = clippedLeft; x < clippedRight; x++)
            {
                var destination = surface.GetPixel(x, y);
                var blended = BlendSourceOver(color, destination);
                surface.SetPixel(x, y, blended);
            }
        }
    }

    public static void StrokeRect(RasterSurface surface, Rect rect, Rgba32 color, double thickness, Rect? clip = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ValidateFiniteRect(rect, "Rect coordinates must be finite numbers.", nameof(rect));
        if (!IsFinite(thickness))
        {
            throw new ArgumentException("Thickness must be a finite number.", nameof(thickness));
        }

        if (rect.Width <= 0 || rect.Height <= 0 || thickness <= 0)
        {
            return;
        }

        var pixelThickness = Math.Max(1, (int)Math.Ceiling(thickness));
        FillRect(surface, new Rect(rect.X, rect.Y, rect.Width, pixelThickness), color, clip);
        FillRect(surface, new Rect(rect.X, rect.Y + rect.Height - pixelThickness, rect.Width, pixelThickness), color, clip);

        var sideHeight = rect.Height - (2 * pixelThickness);
        if (sideHeight <= 0)
        {
            return;
        }

        FillRect(surface, new Rect(rect.X, rect.Y + pixelThickness, pixelThickness, sideHeight), color, clip);
        FillRect(surface, new Rect(rect.X + rect.Width - pixelThickness, rect.Y + pixelThickness, pixelThickness, sideHeight), color, clip);
    }

    private static void ValidateFiniteRect(Rect rect, string message, string paramName)
    {
        if (!IsFinite(rect.X) || !IsFinite(rect.Y) || !IsFinite(rect.Width) || !IsFinite(rect.Height))
        {
            throw new ArgumentException(message, paramName);
        }
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static Rgba32 BlendSourceOver(Rgba32 source, Rgba32 destination)
    {
        if (source.A == 255)
        {
            return source;
        }

        if (source.A == 0)
        {
            return destination;
        }

        var sourceAlpha = source.A;
        var destinationAlpha = destination.A;
        var inverseSourceAlpha = 255 - sourceAlpha;

        var outputAlpha = sourceAlpha + DivideBy255(destinationAlpha * inverseSourceAlpha);

        if (outputAlpha == 0)
        {
            return Rgba32.Transparent;
        }

        var sourcePremultipliedRed = source.R * sourceAlpha;
        var sourcePremultipliedGreen = source.G * sourceAlpha;
        var sourcePremultipliedBlue = source.B * sourceAlpha;

        var destinationPremultipliedRed = destination.R * destinationAlpha;
        var destinationPremultipliedGreen = destination.G * destinationAlpha;
        var destinationPremultipliedBlue = destination.B * destinationAlpha;

        var outputPremultipliedRed = sourcePremultipliedRed + DivideBy255(destinationPremultipliedRed * inverseSourceAlpha);
        var outputPremultipliedGreen = sourcePremultipliedGreen + DivideBy255(destinationPremultipliedGreen * inverseSourceAlpha);
        var outputPremultipliedBlue = sourcePremultipliedBlue + DivideBy255(destinationPremultipliedBlue * inverseSourceAlpha);

        var outputRed = DivideByOutputAlpha(outputPremultipliedRed, outputAlpha);
        var outputGreen = DivideByOutputAlpha(outputPremultipliedGreen, outputAlpha);
        var outputBlue = DivideByOutputAlpha(outputPremultipliedBlue, outputAlpha);

        return new Rgba32((byte)outputRed, (byte)outputGreen, (byte)outputBlue, (byte)outputAlpha);
    }

    private static int DivideBy255(int value)
    {
        return (value + 127) / 255;
    }

    private static int DivideByOutputAlpha(int value, int alpha)
    {
        return (value + (alpha / 2)) / alpha;
    }
}
