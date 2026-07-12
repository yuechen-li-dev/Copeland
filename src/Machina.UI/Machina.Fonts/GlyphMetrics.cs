namespace Machina.Fonts;

public sealed record GlyphMetrics
{
    public GlyphMetrics(double advance, double bearingX, double bearingY, double width, double height)
    {
        ValidateFinite(advance, nameof(advance));
        ValidateFinite(bearingX, nameof(bearingX));
        ValidateFinite(bearingY, nameof(bearingY));
        ValidateFinite(width, nameof(width));
        ValidateFinite(height, nameof(height));

        if (advance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(advance));
        }

        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Advance = advance;
        BearingX = bearingX;
        BearingY = bearingY;
        Width = width;
        Height = height;
    }

    public double Advance { get; }

    public double BearingX { get; }

    public double BearingY { get; }

    public double Width { get; }

    public double Height { get; }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "Metric values must be finite.");
        }
    }
}
