using System.Text;
using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public static class DistanceFieldTextLayout
{
    public static DistanceFieldTextLayoutResult Layout(
        DistanceFieldTextRun run,
        IReadOnlyDictionary<GlyphKey, GlyphMetrics> metricsByGlyph,
        DistanceFieldTextRenderOptions options,
        IReadOnlyList<FontGenerationDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(metricsByGlyph);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        List<DistanceFieldGlyphPlacement> placements = [];
        List<FontGenerationDiagnostic> allDiagnostics = diagnostics is null ? [] : [.. diagnostics];

        double scale = 1d;
        double penX = options.X;
        foreach (GlyphKey key in run.GlyphKeys)
        {
            if (!metricsByGlyph.TryGetValue(key, out GlyphMetrics? metrics))
            {
                allDiagnostics.Add(new FontGenerationDiagnostic(
                    FontGenerationDiagnosticSeverity.Error,
                    FontGenerationDiagnosticCode.MissingGlyph,
                    $"No glyph metrics are available for U+{key.Codepoint:X4}.",
                    key));
                continue;
            }

            bool isWhitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));
            placements.Add(new DistanceFieldGlyphPlacement(key, metrics, penX, options.BaselineY, scale, isWhitespace));
            penX += metrics.Advance * scale;
        }

        double width = Math.Max(0d, penX - options.X);
        return new DistanceFieldTextLayoutResult(
            placements,
            width,
            options.EmSize,
            allDiagnostics);
    }
}
