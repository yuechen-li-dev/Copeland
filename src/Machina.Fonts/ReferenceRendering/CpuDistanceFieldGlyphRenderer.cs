using Machina.Fonts;

namespace Machina.Fonts.ReferenceRendering;

public static class CpuDistanceFieldGlyphRenderer
{
    public static RgbaImage RenderGlyph(
        DistanceFieldPageReference page,
        GlyphAtlasEntry entry,
        DistanceFieldRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (entry.PageIndex != page.PageIndex)
        {
            throw new ArgumentException("Glyph entry page index does not match the supplied page.", nameof(entry));
        }

        if (entry.X < 0 || entry.Y < 0 || entry.X + entry.Width > page.Width || entry.Y + entry.Height > page.Height)
        {
            throw new ArgumentException("Glyph entry rectangle must fit inside the supplied page.", nameof(entry));
        }

        RgbaImage image = new(options.OutputWidth, options.OutputHeight);
        double scaleX = options.OutputWidth / (double)entry.Width;
        double scaleY = options.OutputHeight / (double)entry.Height;
        double scale = Math.Min(scaleX, scaleY);

        for (int y = 0; y < options.OutputHeight; y++)
        {
            double normalizedY = (y + 0.5d) / options.OutputHeight;
            if (options.FlipY)
            {
                normalizedY = 1d - normalizedY;
            }

            double v = Lerp(entry.V0, entry.V1, normalizedY);

            for (int x = 0; x < options.OutputWidth; x++)
            {
                double normalizedX = (x + 0.5d) / options.OutputWidth;
                double u = Lerp(entry.U0, entry.U1, normalizedX);
                float distance = DistanceFieldSampling.SampleDistance(page, u, v);
                double coverage = DistanceFieldSampling.ComputeCoverage(distance, options.PxRange, options.Threshold, scale);
                image.SetPixel(x, y, Composite(options.Background, options.Foreground, coverage));
            }
        }

        return image;
    }

    private static Rgba32 Composite(Rgba32 background, Rgba32 foreground, double coverage)
    {
        double foregroundAlpha = (foreground.A / 255d) * Clamp01(coverage);
        double backgroundAlpha = background.A / 255d;
        double outputAlpha = foregroundAlpha + (backgroundAlpha * (1d - foregroundAlpha));

        if (outputAlpha <= 0d)
        {
            return Rgba32.Transparent;
        }

        byte r = ToByte(((foreground.R / 255d) * foregroundAlpha) + ((background.R / 255d) * backgroundAlpha * (1d - foregroundAlpha)), outputAlpha);
        byte g = ToByte(((foreground.G / 255d) * foregroundAlpha) + ((background.G / 255d) * backgroundAlpha * (1d - foregroundAlpha)), outputAlpha);
        byte b = ToByte(((foreground.B / 255d) * foregroundAlpha) + ((background.B / 255d) * backgroundAlpha * (1d - foregroundAlpha)), outputAlpha);
        byte a = (byte)Math.Round(outputAlpha * 255d, MidpointRounding.AwayFromZero);

        return new Rgba32(r, g, b, a);
    }

    private static byte ToByte(double premultipliedChannel, double outputAlpha)
    {
        double straight = premultipliedChannel / outputAlpha;
        double clamped = Clamp01(straight);
        return (byte)Math.Round(clamped * 255d, MidpointRounding.AwayFromZero);
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + ((b - a) * t);
    }

    private static double Clamp01(double value)
    {
        if (value < 0d)
        {
            return 0d;
        }

        if (value > 1d)
        {
            return 1d;
        }

        return value;
    }
}
