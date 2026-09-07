namespace Aurelian.Profile.Graphics;

public sealed record ProfileEdgeMetrics(
    int OpaquePixels,
    int PartialCoveragePixels,
    int SilhouettePixels,
    double PartialCoveragePerSilhouette,
    double MeanEdgeGradient,
    double EstimatedTransitionWidthPixels);

public sealed record ProfileImageComparison(
    double SilhouetteIntersectionOverUnion,
    double MeanAbsoluteChannelError,
    int DifferingSilhouettePixels);

public static class ProfileEdgeDiagnostics
{
    public static ProfileEdgeMetrics Measure(ReadOnlySpan<byte> rgbaPixels, int width, int height)
    {
        Validate(rgbaPixels, width, height);
        int opaque = 0;
        int partial = 0;
        int silhouette = 0;
        double gradient = 0;
        int gradientSamples = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte alpha = Alpha(rgbaPixels, width, x, y);
                if (alpha >= 250)
                {
                    opaque++;
                }
                if (alpha is > 5 and < 250)
                {
                    partial++;
                }
                if (alpha >= 128)
                {
                    silhouette++;
                }
                if (x + 1 < width)
                {
                    gradient += Math.Abs(alpha - Alpha(rgbaPixels, width, x + 1, y)) / 255d;
                    gradientSamples++;
                }
                if (y + 1 < height)
                {
                    gradient += Math.Abs(alpha - Alpha(rgbaPixels, width, x, y + 1)) / 255d;
                    gradientSamples++;
                }
            }
        }

        int boundary = CountSilhouetteBoundary(rgbaPixels, width, height);
        return new ProfileEdgeMetrics(
            opaque,
            partial,
            silhouette,
            silhouette == 0 ? 0 : partial / (double)silhouette,
            gradientSamples == 0 ? 0 : gradient / gradientSamples,
            boundary == 0 ? 0 : partial / (double)boundary);
    }

    public static ProfileImageComparison Compare(
        ReadOnlySpan<byte> reference,
        ReadOnlySpan<byte> actual,
        int width,
        int height)
    {
        Validate(reference, width, height);
        Validate(actual, width, height);
        int intersection = 0;
        int union = 0;
        int silhouetteDifference = 0;
        long channelError = 0;

        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int offset = pixel * 4;
            bool referenceInside = reference[offset + 3] >= 128;
            bool actualInside = actual[offset + 3] >= 128;
            if (referenceInside && actualInside)
            {
                intersection++;
            }
            if (referenceInside || actualInside)
            {
                union++;
            }
            if (referenceInside != actualInside)
            {
                silhouetteDifference++;
            }
            for (int channel = 0; channel < 4; channel++)
            {
                channelError += Math.Abs(reference[offset + channel] - actual[offset + channel]);
            }
        }

        return new ProfileImageComparison(
            union == 0 ? 1 : intersection / (double)union,
            channelError / (double)(width * height * 4),
            silhouetteDifference);
    }

    private static int CountSilhouetteBoundary(ReadOnlySpan<byte> pixels, int width, int height)
    {
        int count = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool inside = Alpha(pixels, width, x, y) >= 128;
                if ((x > 0 && inside != (Alpha(pixels, width, x - 1, y) >= 128))
                    || (y > 0 && inside != (Alpha(pixels, width, x, y - 1) >= 128)))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static byte Alpha(ReadOnlySpan<byte> pixels, int width, int x, int y)
    {
        return pixels[((y * width) + x) * 4 + 3];
    }

    private static void Validate(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("RGBA pixels must exactly match the positive image extent.", nameof(pixels));
        }
    }
}
