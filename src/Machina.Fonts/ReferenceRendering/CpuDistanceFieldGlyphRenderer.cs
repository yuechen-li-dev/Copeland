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
        Fill(image, options.Background);
        RenderGlyphInto(image, page, entry, 0, 0, options.OutputWidth, options.OutputHeight, options);
        return image;
    }

    internal static void RenderGlyphInto(
        RgbaImage image,
        DistanceFieldPageReference page,
        GlyphAtlasEntry entry,
        int destinationX,
        int destinationY,
        int outputWidth,
        int outputHeight,
        DistanceFieldRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateEntry(page, entry);
        ArgumentNullException.ThrowIfNull(options);

        if (outputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputWidth));
        }

        if (outputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputHeight));
        }

        double scaleX = outputWidth / (double)entry.Width;
        double scaleY = outputHeight / (double)entry.Height;
        double scale = Math.Min(scaleX, scaleY);

        for (int y = 0; y < outputHeight; y++)
        {
            int targetY = destinationY + y;
            if ((uint)targetY >= (uint)image.Height)
            {
                continue;
            }

            double normalizedY = (y + 0.5d) / outputHeight;
            if (options.FlipY)
            {
                normalizedY = 1d - normalizedY;
            }

            double v = Lerp(entry.V0, entry.V1, normalizedY);

            for (int x = 0; x < outputWidth; x++)
            {
                int targetX = destinationX + x;
                if ((uint)targetX >= (uint)image.Width)
                {
                    continue;
                }

                double normalizedX = (x + 0.5d) / outputWidth;
                double u = Lerp(entry.U0, entry.U1, normalizedX);
                float distance = DistanceFieldSampling.SampleDistance(page, u, v);
                double coverage = DistanceFieldSampling.ComputeCoverage(distance, options.PxRange, options.Threshold, scale);
                Rgba32 existing = image.GetPixel(targetX, targetY);
                image.SetPixel(targetX, targetY, Composite(existing, options.Foreground, coverage));
            }
        }
    }

    internal static void Fill(RgbaImage image, Rgba32 color)
    {
        ArgumentNullException.ThrowIfNull(image);

        for (int i = 0; i < image.Pixels.Length; i++)
        {
            image.Pixels[i] = color;
        }
    }

    internal static Rgba32 Composite(Rgba32 background, Rgba32 foreground, double coverage)
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

    private static void ValidateEntry(DistanceFieldPageReference page, GlyphAtlasEntry entry)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.PageIndex != page.PageIndex)
        {
            throw new ArgumentException("Glyph entry page index does not match the supplied page.", nameof(entry));
        }

        if (entry.X < 0 || entry.Y < 0 || entry.X + entry.Width > page.Width || entry.Y + entry.Height > page.Height)
        {
            throw new ArgumentException("Glyph entry rectangle must fit inside the supplied page.", nameof(entry));
        }
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
