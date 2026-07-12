namespace Machina.Fonts;

public sealed record GlyphAtlasEntry
{
    public GlyphAtlasEntry(
        GlyphKey key,
        int pageIndex,
        int x,
        int y,
        int width,
        int height,
        double u0,
        double v0,
        double u1,
        double v1,
        GlyphMetrics metrics,
        GlyphFieldPlacement placement)
    {
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0) throw new ArgumentOutOfRangeException(nameof(y));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (!double.IsFinite(u0)) throw new ArgumentOutOfRangeException(nameof(u0));
        if (!double.IsFinite(v0)) throw new ArgumentOutOfRangeException(nameof(v0));
        if (!double.IsFinite(u1)) throw new ArgumentOutOfRangeException(nameof(u1));
        if (!double.IsFinite(v1)) throw new ArgumentOutOfRangeException(nameof(v1));
        if (u0 > u1) throw new ArgumentException("U0 must be less than or equal to U1.", nameof(u0));
        if (v0 > v1) throw new ArgumentException("V0 must be less than or equal to V1.", nameof(v0));

        Key = key;
        PageIndex = pageIndex;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        U0 = u0;
        V0 = v0;
        U1 = u1;
        V1 = v1;
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        Placement = placement ?? throw new ArgumentNullException(nameof(placement));
    }

    public GlyphKey Key { get; }
    public int PageIndex { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public double U0 { get; }
    public double V0 { get; }
    public double U1 { get; }
    public double V1 { get; }
    public GlyphMetrics Metrics { get; }
    public GlyphFieldPlacement Placement { get; }
}
