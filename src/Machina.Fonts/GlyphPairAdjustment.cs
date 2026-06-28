namespace Machina.Fonts;

public sealed record GlyphPairAdjustment
{
    public static readonly GlyphPairAdjustment Zero = new(0d, 0d);

    public GlyphPairAdjustment(double advanceX, double advanceY = 0d)
    {
        ValidateFinite(advanceX, nameof(advanceX));
        ValidateFinite(advanceY, nameof(advanceY));

        AdvanceX = advanceX;
        AdvanceY = advanceY;
    }

    public double AdvanceX { get; }

    public double AdvanceY { get; }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "Pair adjustment values must be finite.");
        }
    }
}
