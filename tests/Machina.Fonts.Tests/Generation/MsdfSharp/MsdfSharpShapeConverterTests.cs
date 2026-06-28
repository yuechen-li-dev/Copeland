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
}
