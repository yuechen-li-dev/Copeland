using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Tests.Generation.Typography;
using Xunit;

namespace Machina.Fonts.Tests.Generation.MsdfSharp;

internal static class MsdfSharpTestHelpers
{
    public static GlyphOutline CreateLineOutline(char value = 'A')
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), value, 32);
        GlyphMetrics metrics = new(18, 0, 22, 18, 22);
        GlyphContour contour = new(
        [
            new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(18, 0)),
            new GlyphLineSegment(new GlyphPoint(18, 0), new GlyphPoint(18, 22)),
            new GlyphLineSegment(new GlyphPoint(18, 22), new GlyphPoint(0, 22)),
            new GlyphLineSegment(new GlyphPoint(0, 22), new GlyphPoint(0, 0)),
        ]);

        return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, 18, 22), [contour]);
    }

    public static GlyphOutline CreateQuadraticOutline(char value = 'a')
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), value, 32);
        GlyphMetrics metrics = new(18, 0, 22, 18, 22);
        GlyphContour contour = new(
        [
            new GlyphQuadraticSegment(new GlyphPoint(0, 10), new GlyphPoint(4, 0), new GlyphPoint(9, 4)),
            new GlyphQuadraticSegment(new GlyphPoint(9, 4), new GlyphPoint(14, 8), new GlyphPoint(18, 4)),
            new GlyphQuadraticSegment(new GlyphPoint(18, 4), new GlyphPoint(14, 22), new GlyphPoint(9, 18)),
            new GlyphQuadraticSegment(new GlyphPoint(9, 18), new GlyphPoint(4, 14), new GlyphPoint(0, 10)),
        ]);

        return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, 18, 22), [contour]);
    }

    public static GlyphOutline CreateCubicOutline(char value = '&')
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), value, 32);
        GlyphMetrics metrics = new(18, 0, 22, 18, 22);
        GlyphContour contour = new(
        [
            new GlyphCubicSegment(new GlyphPoint(3, 0), new GlyphPoint(0, 2), new GlyphPoint(0, 8), new GlyphPoint(5, 10)),
            new GlyphCubicSegment(new GlyphPoint(5, 10), new GlyphPoint(12, 12), new GlyphPoint(12, 20), new GlyphPoint(4, 22)),
            new GlyphCubicSegment(new GlyphPoint(4, 22), new GlyphPoint(14, 20), new GlyphPoint(18, 8), new GlyphPoint(15, 0)),
            new GlyphCubicSegment(new GlyphPoint(15, 0), new GlyphPoint(12, 2), new GlyphPoint(6, 2), new GlyphPoint(3, 0)),
        ]);

        return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, 18, 22), [contour]);
    }

    public static GlyphOutline CreateEmptyVisibleOutline()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("Fake"), 'A', 32);
        GlyphMetrics metrics = new(18, 0, 22, 18, 22);
        return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, 18, 22), []);
    }

    public static MsdfGenerationSettings CreateSettings(
        DistanceFieldKind kind,
        int width = 32,
        int height = 32,
        double pixelRange = 4,
        double scale = 1,
        string edgeColoring = "simple",
        double miterLimit = 2)
    {
        return new MsdfGenerationSettings(kind, width, height, pixelRange, scale, edgeColoring, miterLimit);
    }

    public static string SummarizeShape(MsdfSharpShapeConversion conversion)
    {
        if (conversion.Shape is null)
        {
            return "null";
        }

        return string.Join(
            ";",
            conversion.Shape.Contours.Select(static contour =>
                string.Join(
                    ",",
                    contour.Edges.Select(static edge =>
                        $"{edge.GetType().Name}:{string.Join("|", edge.ControlPoints.Select(static point => $"{point.X:0.####},{point.Y:0.####}"))}"))));
    }

    public static async Task<GlyphOutline> LoadFixtureOutlineAsync(int codepoint)
    {
        TypographyGlyphOutlineSource source = TypographyFixtureFont.CreateSource();
        GlyphOutlineLoadResult result = await source.LoadGlyphOutlineAsync(
            TypographyFixtureFont.Face,
            codepoint,
            new GlyphOutlineLoadOptions(32, 0, GlyphHintingMode.None, normalizeToEm: true));

        Assert.True(result.Success);
        Assert.NotNull(result.Outline);
        return result.Outline;
    }

    public static void AssertFiniteNonUniform(GeneratedGlyphDistanceField field)
    {
        float[] values = field.Data.ToArray();
        Assert.All(values, static value => Assert.True(float.IsFinite(value)));
        float first = values[0];
        Assert.Contains(values, value => value != first);
    }
}
