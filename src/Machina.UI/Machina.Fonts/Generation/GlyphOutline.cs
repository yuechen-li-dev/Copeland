namespace Machina.Fonts.Generation;

public sealed record GlyphOutline
{
    public GlyphOutline(
        GlyphKey key,
        GlyphMetrics metrics,
        GlyphBounds bounds,
        IReadOnlyList<GlyphContour> contours)
    {
        if (!GlyphKey.IsValidCodepoint(key.Codepoint))
        {
            throw new ArgumentException("Glyph key must contain a valid Unicode scalar value.", nameof(key));
        }

        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(contours);

        if (contours.Any(static contour => contour is null))
        {
            throw new ArgumentException("Outline contours must not contain null entries.", nameof(contours));
        }

        Key = key;
        Metrics = metrics;
        Bounds = bounds;
        Contours = [.. contours];
    }

    public GlyphKey Key { get; }

    public GlyphMetrics Metrics { get; }

    public GlyphBounds Bounds { get; }

    public IReadOnlyList<GlyphContour> Contours { get; }
}
