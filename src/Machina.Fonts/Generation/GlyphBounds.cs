namespace Machina.Fonts.Generation;

public sealed record GlyphBounds
{
    public GlyphBounds(double minX, double minY, double maxX, double maxY)
    {
        ValidateFinite(minX, nameof(minX));
        ValidateFinite(minY, nameof(minY));
        ValidateFinite(maxX, nameof(maxX));
        ValidateFinite(maxY, nameof(maxY));

        if (maxX < minX)
        {
            throw new ArgumentOutOfRangeException(nameof(maxX), "MaxX must be greater than or equal to MinX.");
        }

        if (maxY < minY)
        {
            throw new ArgumentOutOfRangeException(nameof(maxY), "MaxY must be greater than or equal to MinY.");
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }

    public double MinY { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "Bounds values must be finite.");
        }
    }
}
