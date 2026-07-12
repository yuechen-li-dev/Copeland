namespace Machina.Fonts.ReferenceRendering;

public static class ExperimentalMsdfSizing
{
    public static int ComputeFieldDimension(double emSize, int minimumDimension = 32)
    {
        if (!double.IsFinite(emSize) || emSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(emSize));
        }

        if (minimumDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDimension));
        }

        int requestedDimension = Math.Max(minimumDimension, (int)Math.Ceiling(emSize));
        int dimension = 1;

        while (dimension < requestedDimension)
        {
            dimension <<= 1;
        }

        return dimension;
    }
}
