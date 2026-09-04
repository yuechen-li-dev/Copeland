using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Msdfgen;
using Xunit;

namespace Machina.Fonts.Tests.Generation.MsdfSharp;

public sealed class MsdfSharpShapeConverterTests
{
    [Fact]
    public void Convert_LineSegmentOutline_CreatesShape()
    {
        MsdfSharpShapeConversion result = MsdfSharpShapeConverter.Convert(MsdfSharpTestHelpers.CreateLineOutline());

        Assert.True(result.Success);
        Assert.NotNull(result.Shape);
        Assert.Single(result.Shape.Contours);
        Assert.Equal(4, result.Shape.EdgeCount());
        Assert.All(result.Shape.Contours.SelectMany(static contour => contour.Edges), static edge => Assert.IsType<LinearSegment>(edge));
    }

    [Fact]
    public void Convert_QuadraticSegmentOutline_CreatesShape()
    {
        MsdfSharpShapeConversion result = MsdfSharpShapeConverter.Convert(MsdfSharpTestHelpers.CreateQuadraticOutline());

        Assert.True(result.Success);
        Assert.NotNull(result.Shape);
        Assert.Single(result.Shape.Contours);
        Assert.Equal(4, result.Shape.EdgeCount());
        Assert.All(result.Shape.Contours.SelectMany(static contour => contour.Edges), static edge => Assert.IsType<QuadraticSegment>(edge));
    }

    [Fact]
    public void Convert_CubicSegmentOutline_CreatesShape()
    {
        MsdfSharpShapeConversion result = MsdfSharpShapeConverter.Convert(MsdfSharpTestHelpers.CreateCubicOutline());

        Assert.True(result.Success);
        Assert.NotNull(result.Shape);
        Assert.Single(result.Shape.Contours);
        Assert.Equal(4, result.Shape.EdgeCount());
        Assert.All(result.Shape.Contours.SelectMany(static contour => contour.Edges), static edge => Assert.IsType<CubicSegment>(edge));
    }

    [Fact]
    public void Convert_RejectsEmptyVisibleOutline()
    {
        MsdfSharpShapeConversion result = MsdfSharpShapeConverter.Convert(MsdfSharpTestHelpers.CreateEmptyVisibleOutline());

        Assert.False(result.Success);
        Assert.Null(result.Shape);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.EmptyOutline);
    }

    [Fact]
    public void Convert_IsDeterministic()
    {
        GlyphOutline outline = MsdfSharpTestHelpers.CreateQuadraticOutline();

        MsdfSharpShapeConversion first = MsdfSharpShapeConverter.Convert(outline);
        MsdfSharpShapeConversion second = MsdfSharpShapeConverter.Convert(outline);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(MsdfSharpTestHelpers.SummarizeShape(first), MsdfSharpTestHelpers.SummarizeShape(second));
    }

    [Fact]
    public void Convert_RemovesOnlyExactlyDegenerateEdges()
    {
        GlyphPoint origin = new(0, 0);
        GlyphContour contour = new(
        [
            new GlyphLineSegment(origin, origin),
            new GlyphQuadraticSegment(origin, origin, origin),
            new GlyphCubicSegment(origin, origin, origin, origin),
            new GlyphLineSegment(origin, new GlyphPoint(0.000001, 0)),
            new GlyphLineSegment(new GlyphPoint(0.000001, 0), origin),
        ]);
        GlyphOutline outline = new(
            GlyphKey.FromChar(new FontFaceId("Fake"), '.', 32),
            new GlyphMetrics(1, 0, 1, 1, 1),
            new GlyphBounds(0, 0, 1, 1),
            [contour]);

        MsdfSharpShapeConversion result = MsdfSharpShapeConverter.Convert(outline);

        Assert.True(result.Success);
        Assert.NotNull(result.Shape);
        Assert.Equal(2, result.Shape.EdgeCount());
    }
}
