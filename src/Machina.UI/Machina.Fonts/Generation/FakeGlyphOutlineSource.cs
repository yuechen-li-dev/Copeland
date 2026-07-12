using System.Text;

namespace Machina.Fonts.Generation;

public sealed class FakeGlyphOutlineSource : IGlyphOutlineSource
{
    private readonly HashSet<int> missingCodepoints;

    public FakeGlyphOutlineSource(IEnumerable<int>? missingCodepoints = null)
    {
        this.missingCodepoints = missingCodepoints is null ? [] : [.. missingCodepoints];
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
        if (missingCodepoints.Contains(codepoint))
        {
            FontGenerationDiagnostic diagnostic = new(
                FontGenerationDiagnosticSeverity.Warning,
                FontGenerationDiagnosticCode.MissingGlyph,
                "Glyph is configured as missing in the fake outline source.",
                key);

            GlyphMetrics missingMetrics = CreateMetrics(codepoint, options.EmSize);
            return ValueTask.FromResult(new GlyphOutlineLoadResult(false, null, missingMetrics, [diagnostic]));
        }

        GlyphMetrics metrics = CreateMetrics(codepoint, options.EmSize);
        GlyphOutline outline = CreateOutline(key, metrics, options);
        return ValueTask.FromResult(new GlyphOutlineLoadResult(true, outline, metrics, []));
    }

    private static GlyphOutline CreateOutline(GlyphKey key, GlyphMetrics metrics, GlyphOutlineLoadOptions options)
    {
        if (IsWhitespaceCodepoint(key.Codepoint))
        {
            return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, 0, 0), []);
        }

        if (key.Codepoint == '~')
        {
            return CreateQuadraticOutline(key, metrics, options);
        }

        if (key.Codepoint == '&')
        {
            return CreateCubicOutline(key, metrics, options);
        }

        return CreateLineOutline(key, metrics, options);
    }

    private static GlyphOutline CreateLineOutline(GlyphKey key, GlyphMetrics metrics, GlyphOutlineLoadOptions options)
    {
        double left = 0.10 * options.EmSize;
        double bottom = 0.05 * options.EmSize;
        double right = left + Math.Max(metrics.Width - 2, 1);
        double top = bottom + Math.Max(metrics.Height - 3, 1);

        GlyphContour contour = new(
        [
            new GlyphLineSegment(new GlyphPoint(left, bottom), new GlyphPoint(right, bottom)),
            new GlyphLineSegment(new GlyphPoint(right, bottom), new GlyphPoint(right, top)),
            new GlyphLineSegment(new GlyphPoint(right, top), new GlyphPoint(left, top)),
            new GlyphLineSegment(new GlyphPoint(left, top), new GlyphPoint(left, bottom)),
        ]);

        return new GlyphOutline(
            key,
            metrics,
            new GlyphBounds(left, bottom, right, top),
            [contour]);
    }

    private static GlyphOutline CreateQuadraticOutline(GlyphKey key, GlyphMetrics metrics, GlyphOutlineLoadOptions options)
    {
        double width = Math.Max(metrics.Width - 1, 1);
        double height = Math.Max(metrics.Height - 1, 1);

        GlyphContour contour = new(
        [
            new GlyphQuadraticSegment(
                new GlyphPoint(0, height * 0.25),
                new GlyphPoint(width * 0.25, 0),
                new GlyphPoint(width * 0.5, height * 0.25)),
            new GlyphQuadraticSegment(
                new GlyphPoint(width * 0.5, height * 0.25),
                new GlyphPoint(width * 0.75, height * 0.5),
                new GlyphPoint(width, height * 0.25)),
            new GlyphQuadraticSegment(
                new GlyphPoint(width, height * 0.25),
                new GlyphPoint(width * 0.75, height),
                new GlyphPoint(width * 0.5, height * 0.75)),
            new GlyphQuadraticSegment(
                new GlyphPoint(width * 0.5, height * 0.75),
                new GlyphPoint(width * 0.25, height * 0.5),
                new GlyphPoint(0, height * 0.75)),
        ]);

        return new GlyphOutline(
            key,
            metrics,
            new GlyphBounds(0, 0, width, height),
            [contour]);
    }

    private static GlyphOutline CreateCubicOutline(GlyphKey key, GlyphMetrics metrics, GlyphOutlineLoadOptions options)
    {
        double width = Math.Max(metrics.Width - 1, 1);
        double height = Math.Max(metrics.Height - 1, 1);

        GlyphContour contour = new(
        [
            new GlyphCubicSegment(
                new GlyphPoint(width * 0.2, 0),
                new GlyphPoint(0, height * 0.15),
                new GlyphPoint(0, height * 0.45),
                new GlyphPoint(width * 0.25, height * 0.5)),
            new GlyphCubicSegment(
                new GlyphPoint(width * 0.25, height * 0.5),
                new GlyphPoint(width * 0.55, height * 0.55),
                new GlyphPoint(width * 0.60, height * 0.80),
                new GlyphPoint(width * 0.35, height)),
            new GlyphCubicSegment(
                new GlyphPoint(width * 0.35, height),
                new GlyphPoint(width * 0.85, height * 0.95),
                new GlyphPoint(width, height * 0.30),
                new GlyphPoint(width * 0.70, 0)),
            new GlyphCubicSegment(
                new GlyphPoint(width * 0.70, 0),
                new GlyphPoint(width * 0.55, height * 0.18),
                new GlyphPoint(width * 0.35, height * 0.18),
                new GlyphPoint(width * 0.2, 0)),
        ]);

        return new GlyphOutline(
            key,
            metrics,
            new GlyphBounds(0, 0, width, height),
            [contour]);
    }

    private static GlyphMetrics CreateMetrics(int codepoint, float emSize)
    {
        double widthFactor = GetWidthFactor(codepoint);
        double advance = Math.Ceiling(emSize * widthFactor);
        double width = Math.Max(1, Math.Ceiling(emSize * widthFactor) + 4);
        double height = Math.Max(1, Math.Ceiling(emSize) + 4);
        double bearingY = Math.Ceiling(emSize * 0.8);
        return new GlyphMetrics(advance, 0, bearingY, width, height);
    }

    private static double GetWidthFactor(int codepoint)
    {
        if (codepoint == ' ')
        {
            return 0.35;
        }

        if (codepoint >= '0' && codepoint <= '9')
        {
            return 0.55;
        }

        if (codepoint == '~')
        {
            return 0.70;
        }

        if (codepoint == '&')
        {
            return 0.80;
        }

        if (char.IsPunctuation((char)Math.Min(codepoint, char.MaxValue)))
        {
            return 0.32;
        }

        if (codepoint >= 'A' && codepoint <= 'Z')
        {
            return 0.72;
        }

        if (codepoint >= 'a' && codepoint <= 'z')
        {
            return 0.58;
        }

        return 0.65;
    }

    private static bool IsWhitespaceCodepoint(int codepoint)
    {
        return Rune.TryCreate(codepoint, out Rune rune) && Rune.IsWhiteSpace(rune);
    }
}
