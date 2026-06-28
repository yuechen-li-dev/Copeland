namespace Machina.Fonts.ReferenceRendering;

public sealed record DistanceFieldRenderOptions(
    int OutputWidth,
    int OutputHeight,
    Rgba32 Foreground,
    Rgba32 Background,
    double PxRange = 4.0,
    double Threshold = 0.5,
    double SmoothingMultiplier = 1.0,
    bool FlipY = false)
{
    public DistanceFieldRenderOptions Validate()
    {
        if (OutputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputWidth));
        }

        if (OutputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputHeight));
        }

        if (!double.IsFinite(PxRange) || PxRange <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PxRange));
        }

        if (!double.IsFinite(Threshold) || Threshold < 0 || Threshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Threshold));
        }

        if (!double.IsFinite(SmoothingMultiplier) || SmoothingMultiplier <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(SmoothingMultiplier));
        }

        return this;
    }
}
