using System.Text;
using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public static class DistanceFieldTextLayout
{
    public static DistanceFieldTextLayoutResult Layout(
        DistanceFieldTextRun run,
        IReadOnlyDictionary<GlyphKey, GlyphMetrics> metricsByGlyph,
        DistanceFieldTextRenderOptions options,
        IReadOnlyList<FontGenerationDiagnostic>? diagnostics = null,
        IReadOnlyDictionary<GlyphPairKey, GlyphPairAdjustment>? pairAdjustments = null,
        IReadOnlyDictionary<int, double>? tokenAnchorOrigins = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(metricsByGlyph);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        List<DistanceFieldGlyphPlacement> placements = [];
        List<FontGenerationDiagnostic> allDiagnostics = diagnostics is null ? [] : [.. diagnostics];

        double scale = 1d;
        double penX = options.X;
        GlyphKey? previousKey = null;
        bool previousWasWhitespace = true;
        IReadOnlyList<MachinaTokenPlacement> sourceTokens = MachinaTextTokenizer.Tokenize(run.Text);
        List<MachinaGlyphPlacement> semanticGlyphs = [];
        int sourceIndex = 0;

        foreach (GlyphKey key in run.GlyphKeys)
        {
            int sourceLength = char.ConvertFromUtf32(key.Codepoint).Length;
            int tokenId = FindTokenId(sourceTokens, sourceIndex);

            if (!metricsByGlyph.TryGetValue(key, out GlyphMetrics? metrics))
            {
                allDiagnostics.Add(new FontGenerationDiagnostic(
                    FontGenerationDiagnosticSeverity.Error,
                    FontGenerationDiagnosticCode.MissingGlyph,
                    $"No glyph metrics are available for U+{key.Codepoint:X4}.",
                    key));
                sourceIndex += sourceLength;
                continue;
            }

            bool isWhitespace = Rune.IsWhiteSpace(new Rune(key.Codepoint));

            if (!isWhitespace
                && IsFirstVisibleGlyph(sourceTokens[tokenId], sourceIndex)
                && tokenAnchorOrigins is not null
                && tokenAnchorOrigins.TryGetValue(tokenId, out double anchorOrigin))
            {
                penX = anchorOrigin;
                previousKey = null;
                previousWasWhitespace = true;
            }

            if (previousKey is GlyphKey previous
                && !previousWasWhitespace
                && !isWhitespace
                && pairAdjustments is not null
                && pairAdjustments.TryGetValue(new GlyphPairKey(previous, key), out GlyphPairAdjustment? adjustment))
            {
                penX += adjustment.AdvanceX * scale;
            }

            placements.Add(new DistanceFieldGlyphPlacement(key, metrics, penX, options.BaselineY, scale, isWhitespace));
            semanticGlyphs.Add(new MachinaGlyphPlacement(
                key,
                GlyphId: null,
                new MachinaTextSpan(sourceIndex, sourceLength),
                penX,
                options.BaselineY,
                metrics.Advance * scale,
                new MachinaPlaneBounds(
                    metrics.BearingX * scale,
                    -metrics.BearingY * scale,
                    (metrics.BearingX + metrics.Width) * scale,
                    (metrics.Height - metrics.BearingY) * scale),
                tokenId,
                isWhitespace));
            penX += metrics.Advance * scale;
            previousKey = key;
            previousWasWhitespace = isWhitespace;
            sourceIndex += sourceLength;
        }

        double width = Math.Max(0d, penX - options.X);
        IReadOnlyList<MachinaTokenPlacement> positionedTokens = PositionTokens(sourceTokens, semanticGlyphs);
        MachinaPlaneBounds? inkBounds = UnionInkBounds(semanticGlyphs);
        MachinaGlyphRun glyphRun = new(
            run.Text,
            [new MachinaLinePlacement(0, new MachinaTextSpan(0, run.Text.Length), options.BaselineY, width, options.EmSize, inkBounds)],
            positionedTokens,
            semanticGlyphs);

        return new DistanceFieldTextLayoutResult(
            placements,
            width,
            options.EmSize,
            allDiagnostics,
            glyphRun);
    }

    private static int FindTokenId(IReadOnlyList<MachinaTokenPlacement> tokens, int sourceIndex)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            if (sourceIndex >= tokens[index].SourceSpan.Start && sourceIndex < tokens[index].SourceSpan.End)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Source index {sourceIndex} is outside the tokenized text.");
    }

    private static bool IsFirstVisibleGlyph(MachinaTokenPlacement token, int sourceIndex)
    {
        return token.Kind != MachinaTextTokenKind.Whitespace && token.SourceSpan.Start == sourceIndex;
    }

    private static IReadOnlyList<MachinaTokenPlacement> PositionTokens(
        IReadOnlyList<MachinaTokenPlacement> sourceTokens,
        IReadOnlyList<MachinaGlyphPlacement> glyphs)
    {
        List<MachinaTokenPlacement> result = [];

        foreach (MachinaTokenPlacement token in sourceTokens)
        {
            List<(MachinaGlyphPlacement Glyph, int Index)> tokenGlyphs = glyphs
                .Select(static (glyph, index) => (Glyph: glyph, Index: index))
                .Where(item => item.Glyph.TokenId == token.Id)
                .ToList();

            List<(MachinaGlyphPlacement Glyph, int Index)> visibleGlyphs = tokenGlyphs
                .Where(static item => !item.Glyph.IsWhitespace)
                .ToList();
            (MachinaGlyphPlacement Glyph, int Index)? anchor = visibleGlyphs.Count == 0
                ? null
                : visibleGlyphs[0];

            double advance = tokenGlyphs.Count == 0
                ? 0d
                : (tokenGlyphs[^1].Glyph.OriginX + tokenGlyphs[^1].Glyph.Advance) - tokenGlyphs[0].Glyph.OriginX;
            MachinaPlaneBounds? inkBounds = UnionInkBounds(tokenGlyphs.Select(static item => item.Glyph));

            result.Add(token with
            {
                AnchorGlyphIndex = anchor?.Index,
                AnchorOriginX = anchor?.Glyph.OriginX,
                AnchorOriginY = anchor?.Glyph.BaselineY,
                AdvanceWidth = advance,
                InkBounds = inkBounds,
            });
        }

        return result;
    }

    private static MachinaPlaneBounds? UnionInkBounds(IEnumerable<MachinaGlyphPlacement> glyphs)
    {
        MachinaPlaneBounds? result = null;

        foreach (MachinaGlyphPlacement glyph in glyphs)
        {
            if (glyph.IsWhitespace || glyph.PlaneBounds.Width <= 0d || glyph.PlaneBounds.Height <= 0d)
            {
                continue;
            }

            MachinaPlaneBounds absolute = new(
                glyph.OriginX + glyph.PlaneBounds.Left,
                glyph.BaselineY + glyph.PlaneBounds.Top,
                glyph.OriginX + glyph.PlaneBounds.Right,
                glyph.BaselineY + glyph.PlaneBounds.Bottom);

            result = result is null
                ? absolute
                : new MachinaPlaneBounds(
                    Math.Min(result.Left, absolute.Left),
                    Math.Min(result.Top, absolute.Top),
                    Math.Max(result.Right, absolute.Right),
                    Math.Max(result.Bottom, absolute.Bottom));
        }

        return result;
    }
}
