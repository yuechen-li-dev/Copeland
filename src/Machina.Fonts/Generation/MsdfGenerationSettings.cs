namespace Machina.Fonts.Generation;

public sealed record MsdfGenerationSettings
{
    public MsdfGenerationSettings(
        DistanceFieldKind kind,
        int width,
        int height,
        double pixelRange,
        double scale,
        string edgeColoring,
        double miterLimit)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        if (!double.IsFinite(pixelRange) || pixelRange <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRange), "Pixel range must be finite and greater than zero.");
        }

        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be finite and greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(edgeColoring))
        {
            throw new ArgumentException("Edge coloring must not be empty.", nameof(edgeColoring));
        }

        if (!double.IsFinite(miterLimit) || miterLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(miterLimit), "Miter limit must be finite and greater than zero.");
        }

        Kind = kind;
        Width = width;
        Height = height;
        PixelRange = pixelRange;
        Scale = scale;
        EdgeColoring = edgeColoring;
        MiterLimit = miterLimit;
    }

    public DistanceFieldKind Kind { get; }

    public int Width { get; }

    public int Height { get; }

    public double PixelRange { get; }

    public double Scale { get; }

    public string EdgeColoring { get; }

    public double MiterLimit { get; }
}
