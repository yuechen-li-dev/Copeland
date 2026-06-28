using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public sealed record DistanceFieldGlyphPlacement(
    GlyphKey Key,
    GlyphMetrics Metrics,
    double X,
    double BaselineY,
    double Scale,
    bool IsWhitespace);

public sealed record DistanceFieldTextLayoutResult(
    IReadOnlyList<DistanceFieldGlyphPlacement> Placements,
    double Width,
    double Height,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);
