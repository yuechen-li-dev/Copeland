using Aurelian.Rendering.Contracts.Resolved2D;

namespace Aurelian.Rendering.Raster;

internal sealed class RasterBuffer
{
    private readonly Resolved2DRgbaColor[] pixels;

    public RasterBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        pixels = new Resolved2DRgbaColor[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public void FillRectangle(Resolved2DRectangle rectangle, Resolved2DRgbaColor color, PixelBounds clip)
    {
        if (rectangle.IsEmptyOrNegative || clip.IsEmpty)
        {
            return;
        }

        PixelBounds bounds = PixelBounds.Intersect(
            PixelBounds.FromRectangle(rectangle),
            PixelBounds.Intersect(clip, PixelBounds.FromSurface(Width, Height)));

        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            var rowStart = y * Width;
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                var index = rowStart + x;
                pixels[index] = BlendSourceOver(color, pixels[index]);
            }
        }
    }

    public void StrokeRectangle(Resolved2DRectangle rectangle, Resolved2DRgbaColor color, double thickness, PixelBounds clip)
    {
        if (rectangle.IsEmptyOrNegative || clip.IsEmpty)
        {
            return;
        }

        var pixelThickness = Math.Max(1, (int)Math.Ceiling(thickness));
        FillRectangle(new Resolved2DRectangle(rectangle.X, rectangle.Y, rectangle.Width, pixelThickness), color, clip);
        FillRectangle(new Resolved2DRectangle(rectangle.X, rectangle.Y + rectangle.Height - pixelThickness, rectangle.Width, pixelThickness), color, clip);

        var sideHeight = rectangle.Height - (2 * pixelThickness);
        if (sideHeight <= 0)
        {
            return;
        }

        FillRectangle(new Resolved2DRectangle(rectangle.X, rectangle.Y + pixelThickness, pixelThickness, sideHeight), color, clip);
        FillRectangle(new Resolved2DRectangle(rectangle.X + rectangle.Width - pixelThickness, rectangle.Y + pixelThickness, pixelThickness, sideHeight), color, clip);
    }

    public void FillAnalyticShape(AnalyticShapeOperation shape, PixelBounds clip)
    {
        if (shape.Rectangle.IsEmptyOrNegative || clip.IsEmpty)
        {
            return;
        }
        PixelBounds bounds = PixelBounds.Intersect(
            PixelBounds.FromRectangle(shape.Rectangle),
            PixelBounds.Intersect(clip, PixelBounds.FromSurface(Width, Height)));
        double halfWidth = shape.Rectangle.Width / 2;
        double halfHeight = shape.Rectangle.Height / 2;
        double radius = shape.Kind switch
        {
            Resolved2DAnalyticShapeKind.Circle => halfWidth,
            Resolved2DAnalyticShapeKind.Pill => Math.Min(halfWidth, halfHeight),
            _ => shape.Radius,
        };
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                double pX = x + 0.5 - (shape.Rectangle.X + halfWidth);
                double pY = y + 0.5 - (shape.Rectangle.Y + halfHeight);
                double distance = shape.Kind == Resolved2DAnalyticShapeKind.Circle
                    ? Math.Sqrt((pX * pX) + (pY * pY)) - radius
                    : SignedDistanceRoundedRect(pX, pY, halfWidth, halfHeight, radius);
                double coverage = Smooth(Math.Clamp(0.5 - distance, 0, 1));
                if (coverage <= 0)
                {
                    continue;
                }
                double borderMix = Math.Clamp(distance + shape.BorderWidth + 0.5, 0, 1);
                Resolved2DRgbaColor color = Mix(shape.FillColor, shape.BorderColor, borderMix, coverage);
                int index = (y * Width) + x;
                pixels[index] = BlendSourceOver(color, pixels[index]);
            }
        }
    }

    private static double SignedDistanceRoundedRect(
        double pX,
        double pY,
        double halfWidth,
        double halfHeight,
        double radius)
    {
        double qX = Math.Abs(pX) - (halfWidth - radius);
        double qY = Math.Abs(pY) - (halfHeight - radius);
        double outsideX = Math.Max(qX, 0);
        double outsideY = Math.Max(qY, 0);
        return Math.Sqrt((outsideX * outsideX) + (outsideY * outsideY))
            + Math.Min(Math.Max(qX, qY), 0)
            - radius;
    }

    private static double Smooth(double value)
        => value * value * (3 - (2 * value));

    private static Resolved2DRgbaColor Mix(
        Resolved2DRgbaColor fill,
        Resolved2DRgbaColor border,
        double borderMix,
        double coverage)
    {
        static byte Channel(byte left, byte right, double amount)
            => (byte)Math.Round(left + ((right - left) * amount), MidpointRounding.AwayFromZero);
        return new Resolved2DRgbaColor(
            Channel(fill.R, border.R, borderMix),
            Channel(fill.G, border.G, borderMix),
            Channel(fill.B, border.B, borderMix),
            (byte)Math.Round(Channel(fill.A, border.A, borderMix) * coverage, MidpointRounding.AwayFromZero));
    }

    public RasterSurface Complete()
    {
        return new RasterSurface(Width, Height, (Resolved2DRgbaColor[])pixels.Clone());
    }

    private static Resolved2DRgbaColor BlendSourceOver(Resolved2DRgbaColor source, Resolved2DRgbaColor destination)
    {
        if (source.A == byte.MaxValue)
        {
            return source;
        }

        if (source.A == 0)
        {
            return destination;
        }

        var sourceAlpha = source.A;
        var destinationAlpha = destination.A;
        var inverseSourceAlpha = byte.MaxValue - sourceAlpha;
        var outputAlpha = sourceAlpha + DivideBy255(destinationAlpha * inverseSourceAlpha);

        if (outputAlpha == 0)
        {
            return Resolved2DRgbaColor.Transparent;
        }

        var outputRed = DivideByOutputAlpha(
            (source.R * sourceAlpha) + DivideBy255(destination.R * destinationAlpha * inverseSourceAlpha),
            outputAlpha);
        var outputGreen = DivideByOutputAlpha(
            (source.G * sourceAlpha) + DivideBy255(destination.G * destinationAlpha * inverseSourceAlpha),
            outputAlpha);
        var outputBlue = DivideByOutputAlpha(
            (source.B * sourceAlpha) + DivideBy255(destination.B * destinationAlpha * inverseSourceAlpha),
            outputAlpha);

        return new Resolved2DRgbaColor((byte)outputRed, (byte)outputGreen, (byte)outputBlue, (byte)outputAlpha);
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
