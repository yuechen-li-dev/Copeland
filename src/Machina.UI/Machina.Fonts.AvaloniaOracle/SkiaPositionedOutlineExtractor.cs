using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using SkiaSharp;

namespace Machina.Fonts.AvaloniaOracle;

internal static class SkiaPositionedOutlineExtractor
{
    public static IReadOnlyList<PositionedGlyphOutline> Extract(
        string fontPath,
        int faceIndex,
        double fontSize,
        string text,
        IReadOnlyList<AvaloniaReferenceGlyph> glyphs)
    {
        using SKTypeface typeface = SKTypeface.FromFile(fontPath, faceIndex)
            ?? throw new InvalidOperationException("Skia could not load the exact reference font bytes.");
        using SKFont font = new(typeface, checked((float)fontSize));
        List<PositionedGlyphOutline> result = [];

        foreach (AvaloniaReferenceGlyph glyph in glyphs)
        {
            using SKPath? path = font.GetGlyphPath(glyph.GlyphId);
            IReadOnlyList<GlyphContour> contours = path is null
                ? Array.Empty<GlyphContour>()
                : ConvertPath(path, glyph.OriginX, glyph.OriginY);
            int sourceLength = glyph.Cluster >= 0 && glyph.Cluster < text.Length
                ? char.IsHighSurrogate(text[glyph.Cluster]) && glyph.Cluster + 1 < text.Length ? 2 : 1
                : 0;

            result.Add(PositionedOutlineGeometry.FromComparisonSpace(
                glyph.GlyphId,
                new MachinaTextSpan(glyph.Cluster, sourceLength),
                contours,
                fontSize / typeface.UnitsPerEm,
                glyph.OffsetX,
                glyph.OffsetY,
                glyph.OriginX,
                glyph.OriginY,
                glyph.OriginY - glyph.OffsetY,
                "Skia glyph path is Y-down; comparisonY = originY + pathY"));
        }

        return result;
    }

    private static IReadOnlyList<GlyphContour> ConvertPath(SKPath path, double originX, double originY)
    {
        List<GlyphContour> contours = [];
        List<GlyphOutlineSegment> segments = [];
        GlyphPoint? start = null;
        GlyphPoint? current = null;
        SKPoint[] points = new SKPoint[4];

        using SKPath.RawIterator iterator = path.CreateRawIterator();
        while (true)
        {
            SKPathVerb verb = iterator.Next(points);
            if (verb == SKPathVerb.Done)
            {
                FinishContour(contours, segments, start, current);
                break;
            }

            switch (verb)
            {
                case SKPathVerb.Move:
                    FinishContour(contours, segments, start, current);
                    segments = [];
                    start = current = Transform(points[0], originX, originY);
                    break;
                case SKPathVerb.Line:
                    {
                        GlyphPoint p0 = Transform(points[0], originX, originY);
                        GlyphPoint p1 = Transform(points[1], originX, originY);
                        segments.Add(new GlyphLineSegment(p0, p1));
                        current = p1;
                        break;
                    }
                case SKPathVerb.Quad:
                    {
                        GlyphPoint p0 = Transform(points[0], originX, originY);
                        GlyphPoint p1 = Transform(points[1], originX, originY);
                        GlyphPoint p2 = Transform(points[2], originX, originY);
                        segments.Add(new GlyphQuadraticSegment(p0, p1, p2));
                        current = p2;
                        break;
                    }
                case SKPathVerb.Cubic:
                    {
                        GlyphPoint p0 = Transform(points[0], originX, originY);
                        GlyphPoint p1 = Transform(points[1], originX, originY);
                        GlyphPoint p2 = Transform(points[2], originX, originY);
                        GlyphPoint p3 = Transform(points[3], originX, originY);
                        segments.Add(new GlyphCubicSegment(p0, p1, p2, p3));
                        current = p3;
                        break;
                    }
                case SKPathVerb.Close:
                    FinishContour(contours, segments, start, current);
                    segments = [];
                    start = null;
                    current = null;
                    break;
                case SKPathVerb.Conic:
                    throw new InvalidOperationException("Conic Skia glyph paths are not expected for the qualified TrueType face.");
                default:
                    throw new InvalidOperationException($"Unsupported Skia path verb {verb}.");
            }
        }

        return contours;
    }

    private static void FinishContour(
        List<GlyphContour> contours,
        List<GlyphOutlineSegment> segments,
        GlyphPoint? start,
        GlyphPoint? current)
    {
        if (start is GlyphPoint first && current is GlyphPoint last && first != last)
        {
            segments.Add(new GlyphLineSegment(last, first));
        }

        if (segments.Count > 0)
        {
            contours.Add(new GlyphContour([.. segments]));
        }
    }

    private static GlyphPoint Transform(SKPoint point, double originX, double originY)
    {
        return new GlyphPoint(originX + point.X, originY + point.Y);
    }
}
