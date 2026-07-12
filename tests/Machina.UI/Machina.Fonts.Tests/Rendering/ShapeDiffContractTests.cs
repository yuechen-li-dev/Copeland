using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class ShapeDiffContractTests
{
    [Fact]
    public void OutlineFlattening_QuadraticSegment_IsStable()
    {
        GlyphContour contour = new([
            new GlyphQuadraticSegment(
                new GlyphPoint(0, 0),
                new GlyphPoint(2, 4),
                new GlyphPoint(4, 0)),
        ]);

        FlattenedGlyphContour flattened = OutlineFlattening.FlattenContour(
            contour,
            new OutlineFlatteningOptions(4));

        Assert.Equal(5, flattened.Points.Count);
        AssertPoint(flattened.Points[0], 0, 0);
        AssertPoint(flattened.Points[2], 2, 2);
        AssertPoint(flattened.Points[4], 4, 0);
    }

    [Fact]
    public void DirectOutlineMask_HoleUsesDocumentedFillRule()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("test"), 'O', 32);
        GlyphOutline outline = CreateHoledRectangleOutline(key);
        DistanceFieldTextLayoutResult layout = new(
            [new DistanceFieldGlyphPlacement(key, outline.Metrics, 2, 10, 1d, false)],
            outline.Metrics.Advance,
            key.EmSize,
            []);

        InkMask mask = DirectOutlineMaskRenderer.RenderMask(
            new Dictionary<GlyphKey, GlyphOutline> { [key] = outline },
            layout,
            new DirectOutlineMaskRenderOptions(
                16,
                16,
                Rgba32.White,
                Rgba32.Black,
                2,
                10,
                Supersample: 1));

        Assert.True(mask.IsInk(2, 4));
        Assert.False(mask.IsInk(5, 7));
    }

    [Fact]
    public void InkMask_IgnoresBaselineGuideColor()
    {
        RgbaImage image = new(8, 8);
        CpuDistanceFieldGlyphRenderer.Fill(image, new Rgba32(16, 16, 24, 255));
        FillRow(image, 4, new Rgba32(255, 0, 0, 255));
        image.SetPixel(5, 2, new Rgba32(240, 240, 240, 255));

        InkMask mask = InkMask.FromImage(
            image,
            new InkMaskExtractionOptions(
                new Rgba32(16, 16, 24, 255),
                new Rgba32(255, 0, 0, 255)));

        Assert.True(mask.IsInk(5, 2));
        Assert.False(mask.IsInk(0, 4));
    }

    [Fact]
    public void ShapeDiff_IdenticalMasksHavePerfectIntersectionOverUnion()
    {
        InkMask left = CreateRectMask(8, 8, 1, 1, 3, 3);
        InkMask right = CreateRectMask(8, 8, 1, 1, 3, 3);

        ShapeDiffMetrics metrics = InkMaskDiff.Compare(left, right, baselineY: 6);

        Assert.Equal(1d, metrics.IntersectionOverUnion);
        Assert.Equal(0d, metrics.MeanEdgeDistance);
        Assert.Equal(0, metrics.LeftOnlyArea);
        Assert.Equal(0, metrics.RightOnlyArea);
    }

    [Fact]
    public void ShapeDiff_ShiftedMasksReportDistance()
    {
        InkMask left = CreateRectMask(8, 8, 1, 1, 3, 3);
        InkMask right = CreateRectMask(8, 8, 2, 1, 4, 3);

        ShapeDiffMetrics metrics = InkMaskDiff.Compare(left, right, baselineY: 6);

        Assert.Equal(1, metrics.DeltaLeft);
        Assert.True(metrics.MeanEdgeDistance > 0d);
        Assert.True(metrics.P95EdgeDistance >= 1d);
    }

    private static GlyphOutline CreateHoledRectangleOutline(GlyphKey key)
    {
        GlyphMetrics metrics = new(6, 0, 6, 6, 6);
        GlyphContour outer = CreateRectangleContour(0, 0, 6, 6);
        GlyphContour inner = CreateRectangleContour(2, 2, 4, 4);

        return new GlyphOutline(
            key,
            metrics,
            new GlyphBounds(0, 0, 6, 6),
            [outer, inner]);
    }

    private static GlyphContour CreateRectangleContour(
        double left,
        double top,
        double right,
        double bottom)
    {
        return new GlyphContour([
            new GlyphLineSegment(new GlyphPoint(left, top), new GlyphPoint(right, top)),
            new GlyphLineSegment(new GlyphPoint(right, top), new GlyphPoint(right, bottom)),
            new GlyphLineSegment(new GlyphPoint(right, bottom), new GlyphPoint(left, bottom)),
            new GlyphLineSegment(new GlyphPoint(left, bottom), new GlyphPoint(left, top)),
        ]);
    }

    private static InkMask CreateRectMask(
        int width,
        int height,
        int left,
        int top,
        int right,
        int bottom)
    {
        InkMask mask = new(width, height);
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                mask.SetCoverage(x, y, 1f);
            }
        }

        return mask;
    }

    private static void FillRow(RgbaImage image, int y, Rgba32 color)
    {
        for (int x = 0; x < image.Width; x++)
        {
            image.SetPixel(x, y, color);
        }
    }

    private static void AssertPoint(GlyphPoint point, double x, double y)
    {
        Assert.True(Math.Abs(point.X - x) < 0.0001d, $"Expected X={x}, actual={point.X}.");
        Assert.True(Math.Abs(point.Y - y) < 0.0001d, $"Expected Y={y}, actual={point.Y}.");
    }
}
