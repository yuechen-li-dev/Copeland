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
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics,
    MachinaGlyphRun GlyphRun)
{
    public DistanceFieldTextLayoutResult(
        IReadOnlyList<DistanceFieldGlyphPlacement> placements,
        double width,
        double height,
        IReadOnlyList<FontGenerationDiagnostic> diagnostics)
        : this(
            placements,
            width,
            height,
            diagnostics,
            new MachinaGlyphRun(
                string.Empty,
                Array.Empty<MachinaLinePlacement>(),
                Array.Empty<MachinaTokenPlacement>(),
                Array.Empty<MachinaGlyphPlacement>()))
    {
    }
}
