namespace Machina.Fonts.Generation;

public abstract record GlyphOutlineSegment;

public sealed record GlyphLineSegment(
    GlyphPoint P0,
    GlyphPoint P1) : GlyphOutlineSegment;

public sealed record GlyphQuadraticSegment(
    GlyphPoint P0,
    GlyphPoint P1,
    GlyphPoint P2) : GlyphOutlineSegment;

public sealed record GlyphCubicSegment(
    GlyphPoint P0,
    GlyphPoint P1,
    GlyphPoint P2,
    GlyphPoint P3) : GlyphOutlineSegment;
