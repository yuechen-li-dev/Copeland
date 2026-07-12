namespace Machina.Fonts;

public readonly record struct GlyphPairKey
{
    public GlyphPairKey(GlyphKey left, GlyphKey right)
    {
        Left = left;
        Right = right;
    }

    public GlyphKey Left { get; }

    public GlyphKey Right { get; }
}
