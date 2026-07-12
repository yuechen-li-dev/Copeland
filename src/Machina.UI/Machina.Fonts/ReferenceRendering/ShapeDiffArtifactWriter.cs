namespace Machina.Fonts.ReferenceRendering;

public static class ShapeDiffArtifactWriter
{
    public static RgbaImage CreatePairwiseOverlay(
        InkMask left,
        InkMask right,
        Rgba32 background,
        Rgba32 leftOnlyColor,
        Rgba32 rightOnlyColor,
        Rgba32 overlapColor,
        double baselineY,
        Rgba32? baselineColor = null,
        float threshold = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ValidateSameSize(left, right);

        RgbaImage image = new(left.Width, left.Height);
        CpuDistanceFieldGlyphRenderer.Fill(image, background);

        for (int y = 0; y < left.Height; y++)
        {
            for (int x = 0; x < left.Width; x++)
            {
                bool leftInk = left.IsInk(x, y, threshold);
                bool rightInk = right.IsInk(x, y, threshold);

                image.SetPixel(
                    x,
                    y,
                    leftInk && rightInk
                        ? overlapColor
                        : leftInk
                            ? leftOnlyColor
                            : rightInk
                                ? rightOnlyColor
                                : background);
            }
        }

        DrawBaseline(image, baselineY, baselineColor);
        return image;
    }

    public static RgbaImage CreateThreeWayOverlay(
        InkMask browser,
        InkMask directOutline,
        InkMask msdf,
        Rgba32 background,
        Rgba32 browserOnlyColor,
        Rgba32 directOnlyColor,
        Rgba32 msdfOnlyColor,
        Rgba32 overlapColor,
        double baselineY,
        Rgba32? baselineColor = null,
        float threshold = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(directOutline);
        ArgumentNullException.ThrowIfNull(msdf);
        ValidateSameSize(browser, directOutline);
        ValidateSameSize(browser, msdf);

        RgbaImage image = new(browser.Width, browser.Height);
        CpuDistanceFieldGlyphRenderer.Fill(image, background);

        for (int y = 0; y < browser.Height; y++)
        {
            for (int x = 0; x < browser.Width; x++)
            {
                bool browserInk = browser.IsInk(x, y, threshold);
                bool directInk = directOutline.IsInk(x, y, threshold);
                bool msdfInk = msdf.IsInk(x, y, threshold);
                int count = (browserInk ? 1 : 0) + (directInk ? 1 : 0) + (msdfInk ? 1 : 0);

                image.SetPixel(x, y, count switch
                {
                    3 => overlapColor,
                    2 when browserInk && directInk => Blend(browserOnlyColor, directOnlyColor),
                    2 when browserInk && msdfInk => Blend(browserOnlyColor, msdfOnlyColor),
                    2 when directInk && msdfInk => Blend(directOnlyColor, msdfOnlyColor),
                    1 when browserInk => browserOnlyColor,
                    1 when directInk => directOnlyColor,
                    1 when msdfInk => msdfOnlyColor,
                    _ => background,
                });
            }
        }

        DrawBaseline(image, baselineY, baselineColor);
        return image;
    }

    private static void DrawBaseline(RgbaImage image, double baselineY, Rgba32? baselineColor)
    {
        if (baselineColor is null)
        {
            return;
        }

        int baselineRow = (int)Math.Round(baselineY, MidpointRounding.AwayFromZero);
        if ((uint)baselineRow >= (uint)image.Height)
        {
            return;
        }

        for (int x = 0; x < image.Width; x++)
        {
            image.SetPixel(x, baselineRow, baselineColor.Value);
        }
    }

    private static Rgba32 Blend(Rgba32 left, Rgba32 right)
    {
        return new Rgba32(
            (byte)((left.R + right.R) / 2),
            (byte)((left.G + right.G) / 2),
            (byte)((left.B + right.B) / 2),
            255);
    }

    private static void ValidateSameSize(InkMask left, InkMask right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
        {
            throw new InvalidOperationException(
                $"Ink masks must have the same size. Left={left.Width}x{left.Height}, right={right.Width}x{right.Height}.");
        }
    }
}
