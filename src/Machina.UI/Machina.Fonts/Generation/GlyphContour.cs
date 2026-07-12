namespace Machina.Fonts.Generation;

public sealed record GlyphContour
{
    public GlyphContour(IReadOnlyList<GlyphOutlineSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Any(static segment => segment is null))
        {
            throw new ArgumentException("Contour segments must not contain null entries.", nameof(segments));
        }

        Segments = [.. segments];
    }

    public IReadOnlyList<GlyphOutlineSegment> Segments { get; }
}
