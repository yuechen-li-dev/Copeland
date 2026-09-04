using Machina.Fonts.ReferenceRendering;
using Typography.OpenFont;

namespace Machina.Fonts.Generation.Typography;

public sealed record TypographyFontFaceFacts(
    int FaceIndex,
    int UnitsPerEm,
    int Ascender,
    int Descender,
    int LineGap);

public sealed class TypographyGlyphOutlineSource : IGlyphOutlineSource, IGlyphPairAdjustmentSource, IDirectOutlineFontMetricsSource
{
    private readonly TypographyFontFaceCache faceCache;

    public TypographyGlyphOutlineSource(IReadOnlyDictionary<FontFaceId, TypographyFontFaceSource> faces)
    {
        faceCache = new TypographyFontFaceCache(faces);
    }

    public ValueTask<GlyphOutlineLoadResult> LoadGlyphOutlineAsync(
        FontFaceId face,
        int codepoint,
        GlyphOutlineLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!GlyphKey.IsValidCodepoint(codepoint))
        {
            GlyphKey fallbackKey = GlyphKey.FromCodepoint(face, '�', options.EmSize);
            FontGenerationDiagnostic diagnostic = new(
                FontGenerationDiagnosticSeverity.Error,
                FontGenerationDiagnosticCode.InvalidGlyphKey,
                "Codepoint must be a valid Unicode scalar value.",
                fallbackKey);

            return ValueTask.FromResult(new GlyphOutlineLoadResult(false, null, null, [diagnostic]));
        }

        GlyphKey key = GlyphKey.FromCodepoint(face, codepoint, options.EmSize);
        if (!faceCache.TryGetSource(face, out TypographyFontFaceSource? source))
        {
            return ValueTask.FromResult(CreateFailure(
                key,
                null,
                FontGenerationDiagnosticCode.OutlineLoadFailed,
                $"No Typography font face source is configured for '{face}'."));
        }

        if (source.FaceIndex != options.FaceIndex)
        {
            return ValueTask.FromResult(CreateFailure(
                key,
                null,
                FontGenerationDiagnosticCode.InvalidGenerationSettings,
                $"Requested face index {options.FaceIndex} does not match configured face index {source.FaceIndex} for '{face}'."));
        }

        TypographyFontFaceCache.CachedTypeface cachedFace = faceCache.GetOrLoad(face);
        if (!cachedFace.Success || cachedFace.Typeface is null)
        {
            return ValueTask.FromResult(CreateFailure(
                key,
                null,
                FontGenerationDiagnosticCode.OutlineLoadFailed,
                cachedFace.ErrorMessage ?? $"Failed to load font face '{face}'."));
        }

        try
        {
            Typeface typeface = cachedFace.Typeface;
            ushort glyphIndex = typeface.GetGlyphIndex(codepoint);
            if (glyphIndex == 0)
            {
                return ValueTask.FromResult(CreateFailure(
                    key,
                    null,
                    FontGenerationDiagnosticCode.MissingGlyph,
                    $"Glyph U+{codepoint:X4} is not present in '{face}'.",
                    FontGenerationDiagnosticSeverity.Warning));
            }

            Glyph glyph = typeface.GetGlyph(glyphIndex);
            if (glyph.IsCffGlyph)
            {
                return ValueTask.FromResult(CreateFailure(
                    key,
                    null,
                    FontGenerationDiagnosticCode.UnsupportedGlyph,
                    $"Glyph U+{codepoint:X4} uses a CFF outline, which is deferred in the Typography proof adapter."));
            }

            ushort advanceWidth = TrueTypeHorizontalMetricsReader.ReadAdvanceWidth(
                source.Path,
                source.FaceIndex,
                glyphIndex);
            GlyphOutline outline = TypographyOutlineConversion.CreateOutline(
                face,
                codepoint,
                options.EmSize,
                options.NormalizeToEm,
                typeface,
                glyph,
                advanceWidth);

            if (outline.Contours.Count == 0 && !TypographyOutlineConversion.IsWhitespace(codepoint))
            {
                return ValueTask.FromResult(CreateFailure(
                    key,
                    outline.Metrics,
                    FontGenerationDiagnosticCode.EmptyOutline,
                    $"Glyph U+{codepoint:X4} resolved but produced no outline contours."));
            }

            return ValueTask.FromResult(new GlyphOutlineLoadResult(true, outline, outline.Metrics, []));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(CreateFailure(
                key,
                null,
                FontGenerationDiagnosticCode.OutlineLoadFailed,
                $"Failed to extract outline for U+{codepoint:X4} from '{face}': {ex.Message}"));
        }
    }

    public ValueTask<GlyphPairAdjustment?> GetPairAdjustmentAsync(
        GlyphKey left,
        GlyphKey right,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (left.Face != right.Face)
        {
            return ValueTask.FromResult<GlyphPairAdjustment?>(null);
        }

        if (!faceCache.TryGetSource(left.Face, out _))
        {
            return ValueTask.FromResult<GlyphPairAdjustment?>(null);
        }

        TypographyFontFaceCache.CachedTypeface cachedFace = faceCache.GetOrLoad(left.Face);
        if (!cachedFace.Success || cachedFace.Typeface is null)
        {
            return ValueTask.FromResult<GlyphPairAdjustment?>(null);
        }

        GlyphPairAdjustment? adjustment = TypographyGlyphPairAdjustmentEvaluator.Evaluate(
            cachedFace.Typeface,
            left,
            right);

        return ValueTask.FromResult(adjustment);
    }

    public ValueTask<DirectOutlineFontMetricsLoadResult> LoadFontMetricsAsync(
        FontFaceId face,
        double fontSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!double.IsFinite(fontSize) || fontSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        if (!faceCache.TryGetSource(face, out _))
        {
            FontGenerationDiagnostic diagnostic = new(
                FontGenerationDiagnosticSeverity.Error,
                FontGenerationDiagnosticCode.OutlineLoadFailed,
                $"No Typography font face source is configured for '{face}'.");
            return ValueTask.FromResult(new DirectOutlineFontMetricsLoadResult(false, null, false, [diagnostic]));
        }

        TypographyFontFaceCache.CachedTypeface cachedFace = faceCache.GetOrLoad(face);
        if (!cachedFace.Success || cachedFace.Typeface is null)
        {
            FontGenerationDiagnostic diagnostic = new(
                FontGenerationDiagnosticSeverity.Error,
                FontGenerationDiagnosticCode.OutlineLoadFailed,
                cachedFace.ErrorMessage ?? $"Failed to load font face '{face}'.");
            return ValueTask.FromResult(new DirectOutlineFontMetricsLoadResult(false, null, false, [diagnostic]));
        }

        Typeface typeface = cachedFace.Typeface;
        double scale = fontSize / typeface.UnitsPerEm;
        DirectOutlineFontMetrics metrics = new(
            typeface.UnitsPerEm,
            Math.Abs(typeface.Ascender * scale),
            Math.Abs(typeface.Descender * scale),
            Math.Max(0d, typeface.LineGap * scale));

        return ValueTask.FromResult(new DirectOutlineFontMetricsLoadResult(true, metrics, false, []));
    }

    public ushort GetGlyphId(FontFaceId face, int codepoint)
    {
        TypographyFontFaceCache.CachedTypeface cachedFace = faceCache.GetOrLoad(face);
        if (!cachedFace.Success || cachedFace.Typeface is null)
        {
            throw new InvalidOperationException(cachedFace.ErrorMessage ?? $"Failed to load font face '{face}'.");
        }

        return cachedFace.Typeface.GetGlyphIndex(codepoint);
    }

    public TypographyFontFaceFacts GetFaceFacts(FontFaceId face)
    {
        if (!faceCache.TryGetSource(face, out TypographyFontFaceSource? source))
        {
            throw new InvalidOperationException($"No Typography font face source is configured for '{face}'.");
        }

        TypographyFontFaceCache.CachedTypeface cachedFace = faceCache.GetOrLoad(face);
        if (!cachedFace.Success || cachedFace.Typeface is null)
        {
            throw new InvalidOperationException(cachedFace.ErrorMessage ?? $"Failed to load font face '{face}'.");
        }

        Typeface typeface = cachedFace.Typeface;
        return new TypographyFontFaceFacts(
            source.FaceIndex,
            typeface.UnitsPerEm,
            typeface.Ascender,
            typeface.Descender,
            typeface.LineGap);
    }

    private static GlyphOutlineLoadResult CreateFailure(
        GlyphKey key,
        GlyphMetrics? metrics,
        FontGenerationDiagnosticCode code,
        string message,
        FontGenerationDiagnosticSeverity severity = FontGenerationDiagnosticSeverity.Error)
    {
        FontGenerationDiagnostic diagnostic = new(severity, code, message, key);
        return new GlyphOutlineLoadResult(false, null, metrics, [diagnostic]);
    }
}
