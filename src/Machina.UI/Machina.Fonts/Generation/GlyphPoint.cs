namespace Machina.Fonts.Generation;

public readonly record struct GlyphPoint
{
    public GlyphPoint(double x, double y)
    {
        ValidateFinite(x, nameof(x));
        ValidateFinite(y, nameof(y));

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "Point coordinates must be finite.");
        }
    }
}
