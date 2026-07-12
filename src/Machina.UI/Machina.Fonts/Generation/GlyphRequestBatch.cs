namespace Machina.Fonts.Generation;

public sealed record GlyphRequestBatch
{
    public GlyphRequestBatch(IReadOnlyList<GlyphKey> keys)
    {
        Keys = (keys ?? throw new ArgumentNullException(nameof(keys))).Distinct().ToArray();
    }

    public IReadOnlyList<GlyphKey> Keys { get; }
}
