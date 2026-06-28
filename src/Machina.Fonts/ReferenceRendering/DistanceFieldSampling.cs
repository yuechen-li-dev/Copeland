using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public static class DistanceFieldSampling
{
    public static float SampleDistance(
        DistanceFieldPageReference page,
        double u,
        double v)
    {
        ArgumentNullException.ThrowIfNull(page);
        return SampleDistance(page.Data, page.Width, page.Height, page.ChannelCount, page.Kind, u, v);
    }

    public static float SampleDistance(
        ReadOnlySpan<float> data,
        int width,
        int height,
        int channelCount,
        DistanceFieldKind kind,
        double u,
        double v)
    {
        ValidatePageArguments(data, width, height, channelCount, kind);

        double clampedU = Clamp01(u);
        double clampedV = Clamp01(v);
        double sourceX = clampedU * Math.Max(0, width - 1);
        double sourceY = clampedV * Math.Max(0, height - 1);

        int x0 = (int)Math.Floor(sourceX);
        int y0 = (int)Math.Floor(sourceY);
        int x1 = Math.Min(width - 1, x0 + 1);
        int y1 = Math.Min(height - 1, y0 + 1);
        double tx = sourceX - x0;
        double ty = sourceY - y0;

        float topLeft = DecodeDistanceAt(data, width, channelCount, kind, x0, y0);
        float topRight = DecodeDistanceAt(data, width, channelCount, kind, x1, y0);
        float bottomLeft = DecodeDistanceAt(data, width, channelCount, kind, x0, y1);
        float bottomRight = DecodeDistanceAt(data, width, channelCount, kind, x1, y1);

        double top = Lerp(topLeft, topRight, tx);
        double bottom = Lerp(bottomLeft, bottomRight, tx);
        return (float)Lerp(top, bottom, ty);
    }

    public static double ComputeCoverage(
        float distance,
        double pxRange,
        double threshold,
        double scale)
    {
        if (!double.IsFinite(pxRange) || pxRange <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pxRange));
        }

        if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }

        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        double smoothing = 0.5 / Math.Max(1d, pxRange * scale);
        return SmoothStep(threshold - smoothing, threshold + smoothing, distance);
    }

    internal static float DecodeDistanceAt(
        ReadOnlySpan<float> data,
        int width,
        int channelCount,
        DistanceFieldKind kind,
        int x,
        int y)
    {
        int pixelIndex = ((y * width) + x) * channelCount;
        return kind switch
        {
            DistanceFieldKind.Sdf => data[pixelIndex],
            DistanceFieldKind.Psdf => data[pixelIndex],
            DistanceFieldKind.Msdf => Median3(data[pixelIndex], data[pixelIndex + 1], data[pixelIndex + 2]),
            DistanceFieldKind.Mtsdf => Median3(data[pixelIndex], data[pixelIndex + 1], data[pixelIndex + 2]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported distance-field kind."),
        };
    }

    private static void ValidatePageArguments(
        ReadOnlySpan<float> data,
        int width,
        int height,
        int channelCount,
        DistanceFieldKind kind)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        int expectedChannelCount = kind switch
        {
            DistanceFieldKind.Sdf => 1,
            DistanceFieldKind.Psdf => 1,
            DistanceFieldKind.Msdf => 3,
            DistanceFieldKind.Mtsdf => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported distance-field kind."),
        };

        if (channelCount != expectedChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount), $"Channel count must be {expectedChannelCount} for {kind}.");
        }

        int expectedLength = checked(width * height * channelCount);
        if (data.Length != expectedLength)
        {
            throw new ArgumentException($"Page data length must be {expectedLength}.", nameof(data));
        }
    }

    private static float Median3(float a, float b, float c)
    {
        if (a > b)
        {
            (a, b) = (b, a);
        }

        if (b > c)
        {
            (b, c) = (c, b);
        }

        if (a > b)
        {
            b = a;
        }

        return b;
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        if (edge0 >= edge1)
        {
            return value < edge0 ? 0d : 1d;
        }

        double t = Clamp01((value - edge0) / (edge1 - edge0));
        return t * t * (3d - (2d * t));
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + ((b - a) * t);
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value))
        {
            return 0d;
        }

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
