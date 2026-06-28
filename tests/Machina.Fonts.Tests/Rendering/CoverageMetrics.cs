using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tests.Rendering;

internal static class CoverageMetrics
{
    public const double InkThreshold = 0.01d;
    public const double MediumThreshold = 0.10d;
    public const double StrongThreshold = 0.50d;

    public static CoverageScanResult Scan(
        RgbaImage image,
        Rgba32 foreground,
        Rgba32 background,
        double baselineY,
        Rgba32? ignoredColor = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        int inkTop = image.Height;
        int inkBottom = -1;
        int inkLeft = image.Width;
        int inkRight = -1;
        int alphaCoverageCountAbove001 = 0;
        int alphaCoverageCountAbove010 = 0;
        int alphaCoverageCountAbove050 = 0;
        double maxAlpha = 0d;
        double nonZeroAlphaSum = 0d;
        int nonZeroAlphaCount = 0;

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image.GetPixel(x, y);
                if (pixel == background || (ignoredColor.HasValue && pixel == ignoredColor.Value))
                {
                    continue;
                }

                double coverage = DeriveCoverage(pixel, foreground, background);
                if (coverage <= 0d)
                {
                    continue;
                }

                maxAlpha = Math.Max(maxAlpha, coverage);
                nonZeroAlphaSum += coverage;
                nonZeroAlphaCount++;

                if (coverage > InkThreshold)
                {
                    alphaCoverageCountAbove001++;
                    inkTop = Math.Min(inkTop, y);
                    inkBottom = Math.Max(inkBottom, y);
                    inkLeft = Math.Min(inkLeft, x);
                    inkRight = Math.Max(inkRight, x);
                }

                if (coverage > MediumThreshold)
                {
                    alphaCoverageCountAbove010++;
                }

                if (coverage > StrongThreshold)
                {
                    alphaCoverageCountAbove050++;
                }
            }
        }

        if (inkBottom < 0 || inkRight < 0)
        {
            return new CoverageScanResult(
                null,
                null,
                null,
                null,
                0,
                0,
                alphaCoverageCountAbove001,
                alphaCoverageCountAbove010,
                alphaCoverageCountAbove050,
                maxAlpha,
                nonZeroAlphaCount == 0 ? 0d : nonZeroAlphaSum / nonZeroAlphaCount,
                baselineY,
                null);
        }

        return new CoverageScanResult(
            inkTop,
            inkBottom,
            inkLeft,
            inkRight,
            inkBottom - inkTop + 1,
            inkRight - inkLeft + 1,
            alphaCoverageCountAbove001,
            alphaCoverageCountAbove010,
            alphaCoverageCountAbove050,
            maxAlpha,
            nonZeroAlphaSum / nonZeroAlphaCount,
            baselineY,
            inkBottom - baselineY);
    }

    private static double DeriveCoverage(Rgba32 pixel, Rgba32 foreground, Rgba32 background)
    {
        List<double> channels = [];
        AddChannelCoverage(channels, pixel.R, foreground.R, background.R);
        AddChannelCoverage(channels, pixel.G, foreground.G, background.G);
        AddChannelCoverage(channels, pixel.B, foreground.B, background.B);

        if (channels.Count == 0)
        {
            return 0d;
        }

        return Clamp01(channels.Average());
    }

    private static void AddChannelCoverage(List<double> channels, byte pixel, byte foreground, byte background)
    {
        int delta = foreground - background;
        if (delta == 0)
        {
            return;
        }

        channels.Add((pixel - background) / (double)delta);
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

internal sealed record CoverageScanResult(
    int? InkTop,
    int? InkBottom,
    int? InkLeft,
    int? InkRight,
    int InkHeight,
    int InkWidth,
    int AlphaCoverageCountAbove001,
    int AlphaCoverageCountAbove010,
    int AlphaCoverageCountAbove050,
    double MaxAlpha,
    double AverageAlphaNonZero,
    double BaselineY,
    double? DescentBelowBaseline);
