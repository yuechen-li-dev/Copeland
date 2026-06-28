using System.Text;
using Typography.OpenFont;

namespace Machina.Fonts.Generation.Typography;

internal static class TypographyOutlineConversion
{
    public static GlyphOutline CreateOutline(
        FontFaceId face,
        int codepoint,
        float emSize,
        bool normalizeToEm,
        Typeface typeface,
        Glyph glyph)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(glyph);

        double scale = GetScale(typeface, emSize, normalizeToEm);
        GlyphKey key = GlyphKey.FromCodepoint(face, codepoint, emSize);
        GlyphMetrics metrics = CreateMetrics(typeface, glyph, scale);
        GlyphBounds bounds = CreateBounds(glyph, scale);
        IReadOnlyList<GlyphContour> contours = CreateContours(typeface, glyph, scale);
        return new GlyphOutline(key, metrics, bounds, contours);
    }

    public static bool IsWhitespace(int codepoint)
    {
        return Rune.TryCreate(codepoint, out Rune rune) && Rune.IsWhiteSpace(rune);
    }

    public static string BuildContourSummary(GlyphOutline outline)
    {
        StringBuilder builder = new();

        for (int contourIndex = 0; contourIndex < outline.Contours.Count; contourIndex++)
        {
            GlyphContour contour = outline.Contours[contourIndex];
            builder.Append("C");
            builder.Append(contourIndex);
            builder.Append(':');

            for (int segmentIndex = 0; segmentIndex < contour.Segments.Count; segmentIndex++)
            {
                GlyphOutlineSegment segment = contour.Segments[segmentIndex];
                builder.Append(segment switch
                {
                    GlyphLineSegment => "L",
                    GlyphQuadraticSegment => "Q",
                    GlyphCubicSegment => "C",
                    _ => "?",
                });
            }

            builder.Append(';');
        }

        return builder.ToString();
    }

    private static double GetScale(Typeface typeface, float emSize, bool normalizeToEm)
    {
        return normalizeToEm ? emSize / typeface.UnitsPerEm : 1d;
    }

    private static GlyphMetrics CreateMetrics(Typeface typeface, Glyph glyph, double scale)
    {
        double advance = typeface.GetAdvanceWidthFromGlyphIndex(glyph.GlyphIndex) * scale;
        double bearingX = typeface.GetLeftSideBearing(glyph.GlyphIndex) * scale;
        double bearingY = glyph.MaxY * scale;
        double width = Math.Max(0, (glyph.MaxX - glyph.MinX) * scale);
        double height = Math.Max(0, (glyph.MaxY - glyph.MinY) * scale);

        return new GlyphMetrics(advance, bearingX, bearingY, width, height);
    }

    private static GlyphBounds CreateBounds(Glyph glyph, double scale)
    {
        return new GlyphBounds(
            glyph.MinX * scale,
            glyph.MinY * scale,
            glyph.MaxX * scale,
            glyph.MaxY * scale);
    }

    private static IReadOnlyList<GlyphContour> CreateContours(Typeface typeface, Glyph glyph, double scale)
    {
        if (glyph.IsCffGlyph)
        {
            throw new InvalidOperationException("CFF glyph outlines are not yet supported by the Typography proof adapter.");
        }

        OutlineTranslator translator = new();
        IGlyphReaderExtensions.Read(translator, glyph.GlyphPoints, glyph.EndPoints, (float)scale);
        return translator.BuildContours();
    }

    private sealed class OutlineTranslator : IGlyphTranslator
    {
        private readonly List<GlyphContour> contours = [];
        private readonly List<GlyphOutlineSegment> currentSegments = [];
        private GlyphPoint? contourStart;
        private GlyphPoint? currentPoint;

        public void BeginRead(int contourCount)
        {
            ResetCurrentContour();
        }

        public void EndRead()
        {
            FinalizeContour();
        }

        public void MoveTo(float x0, float y0)
        {
            FinalizeContour();

            GlyphPoint start = new(x0, y0);
            contourStart = start;
            currentPoint = start;
        }

        public void LineTo(float x1, float y1)
        {
            EnsureContourStarted();

            GlyphPoint next = new(x1, y1);
            currentSegments.Add(new GlyphLineSegment(currentPoint!.Value, next));
            currentPoint = next;
        }

        public void Curve3(float x1, float y1, float x2, float y2)
        {
            EnsureContourStarted();

            GlyphPoint control = new(x1, y1);
            GlyphPoint end = new(x2, y2);
            currentSegments.Add(new GlyphQuadraticSegment(currentPoint!.Value, control, end));
            currentPoint = end;
        }

        public void Curve4(float x1, float y1, float x2, float y2, float x3, float y3)
        {
            EnsureContourStarted();

            GlyphPoint control1 = new(x1, y1);
            GlyphPoint control2 = new(x2, y2);
            GlyphPoint end = new(x3, y3);
            currentSegments.Add(new GlyphCubicSegment(currentPoint!.Value, control1, control2, end));
            currentPoint = end;
        }

        public void CloseContour()
        {
            FinalizeContour();
        }

        public IReadOnlyList<GlyphContour> BuildContours()
        {
            return contours;
        }

        private void FinalizeContour()
        {
            if (contourStart is not GlyphPoint start || currentPoint is not GlyphPoint current)
            {
                ResetCurrentContour();
                return;
            }

            if (!PointsEqual(current, start))
            {
                currentSegments.Add(new GlyphLineSegment(current, start));
            }

            if (currentSegments.Count > 0)
            {
                contours.Add(new GlyphContour([.. currentSegments]));
            }

            ResetCurrentContour();
        }

        private void EnsureContourStarted()
        {
            if (contourStart is null || currentPoint is null)
            {
                throw new InvalidOperationException("A contour must begin with MoveTo before segments are added.");
            }
        }

        private void ResetCurrentContour()
        {
            currentSegments.Clear();
            contourStart = null;
            currentPoint = null;
        }

        private static bool PointsEqual(GlyphPoint left, GlyphPoint right)
        {
            return left.X == right.X && left.Y == right.Y;
        }
    }
}
