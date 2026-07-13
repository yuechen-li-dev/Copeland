using System.Reflection;
using Aurelian.Rendering.Contracts.Resolved2D;
using Xunit;

namespace Aurelian.Rendering.Raster.Tests;

public sealed class AurelianCpuRasterRendererTests
{
    private readonly AurelianCpuRasterRenderer renderer = new();

    [Fact]
    public void Render_CreatesTransparentStraightRgbaSurface()
    {
        RasterSurface surface = Render(2, 2).Surface;

        Assert.Equal(2, surface.Width);
        Assert.Equal(2, surface.Height);
        Assert.All(surface.Pixels, pixel => Assert.Equal(Resolved2DRgbaColor.Transparent, pixel));
    }

    [Fact]
    public void Render_FillUsesFloorCeilingRounding()
    {
        RasterSurface surface = Render(
            4,
            4,
            new FillRectangleOperation("fill", new Resolved2DRectangle(0.2, 0.2, 1.1, 1.1), Resolved2DRgbaColor.White)).Surface;

        Assert.Equal(Resolved2DRgbaColor.White, surface.GetPixel(0, 0));
        Assert.Equal(Resolved2DRgbaColor.White, surface.GetPixel(1, 1));
        Assert.Equal(Resolved2DRgbaColor.Transparent, surface.GetPixel(2, 2));
    }

    [Fact]
    public void Render_ClipsNestedRectangles()
    {
        RasterSurface surface = Render(
            6,
            6,
            new PushRectangularClipOperation("first", new Resolved2DRectangle(1, 1, 4, 4)),
            new PushRectangularClipOperation("second", new Resolved2DRectangle(3, 0, 4, 4)),
            new FillRectangleOperation("fill", new Resolved2DRectangle(0, 0, 6, 6), Resolved2DRgbaColor.White),
            new PopClipOperation("pop-second"),
            new PopClipOperation("pop-first")).Surface;

        Assert.Equal(Resolved2DRgbaColor.White, surface.GetPixel(3, 1));
        Assert.Equal(Resolved2DRgbaColor.White, surface.GetPixel(4, 3));
        Assert.Equal(Resolved2DRgbaColor.Transparent, surface.GetPixel(2, 1));
        Assert.Equal(Resolved2DRgbaColor.Transparent, surface.GetPixel(3, 4));
    }

    [Fact]
    public void Render_UsesDeterministicSourceOverBlending()
    {
        RasterSurface surface = Render(
            1,
            1,
            new FillRectangleOperation("background", new Resolved2DRectangle(0, 0, 1, 1), Resolved2DRgbaColor.Black),
            new FillRectangleOperation("foreground", new Resolved2DRectangle(0, 0, 1, 1), new Resolved2DRgbaColor(255, 0, 0, 128))).Surface;

        Assert.Equal(new Resolved2DRgbaColor(128, 0, 0, 255), surface.GetPixel(0, 0));
    }

    [Fact]
    public void Render_StrokesInsideRectangleWithCeilingThickness()
    {
        RasterSurface surface = Render(
            5,
            5,
            new StrokeRectangleOperation("stroke", new Resolved2DRectangle(1, 1, 3, 3), Resolved2DRgbaColor.White, 0.2)).Surface;

        Assert.Equal(Resolved2DRgbaColor.White, surface.GetPixel(1, 1));
        Assert.Equal(Resolved2DRgbaColor.White, surface.GetPixel(3, 3));
        Assert.Equal(Resolved2DRgbaColor.Transparent, surface.GetPixel(2, 2));
    }

    [Fact]
    public void Render_PositionedTextSupportsAlignmentAndFallbackGlyphs()
    {
        RasterSurface surface = Render(
            30,
            30,
            new PositionedTextOperation(
                "text",
                new Resolved2DRectangle(0, 0, 25, 21),
                "🙂",
                Resolved2DRgbaColor.White,
                Resolved2DTextFace.ReadableBitmap5x7,
                Resolved2DTextSize.Small,
                Resolved2DTextAlignX.Right,
                Resolved2DTextAlignY.Bottom)).Surface;

        Assert.Equal(Resolved2DRgbaColor.White, surface.GetPixel(22, 14));
    }

    [Fact]
    public void Render_EmptyTextAndNegativeRectangleAreNoOps()
    {
        RasterSurface surface = Render(
            2,
            2,
            new FillRectangleOperation("empty", new Resolved2DRectangle(0, 0, -1, 1), Resolved2DRgbaColor.White),
            new PositionedTextOperation("text", new Resolved2DRectangle(0, 0, 2, 2), string.Empty, Resolved2DRgbaColor.White)).Surface;

        Assert.All(surface.Pixels, pixel => Assert.Equal(Resolved2DRgbaColor.Transparent, pixel));
    }

    [Fact]
    public void Render_RepeatedOutputAndPpmEncodingAreByteIdentical()
    {
        var plan = new Resolved2DPlan(
            new Resolved2DViewport(4, 4),
            [new FillRectangleOperation("fill", new Resolved2DRectangle(0, 0, 4, 4), new Resolved2DRgbaColor(2, 4, 6, 128))]);

        byte[] first = RasterPpmEncoder.EncodeP6(renderer.Render(plan).Surface);
        byte[] second = RasterPpmEncoder.EncodeP6(renderer.Render(plan).Surface);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RasterProductionAssembly_ReferencesOnlyContractsAndFrameworkAssemblies()
    {
        AssemblyName[] references = typeof(AurelianCpuRasterRenderer).Assembly.GetReferencedAssemblies();

        AssemblyName[] nonFrameworkReferences = references
            .Where(reference => !reference.Name!.StartsWith("System", StringComparison.Ordinal))
            .OrderBy(reference => reference.Name)
            .ToArray();

        Assert.Collection(
            nonFrameworkReferences,
            reference => Assert.Equal("Aurelian.Rendering.Contracts", reference.Name));
    }

    private RasterFrame Render(int width, int height, params Resolved2DOperation[] operations)
    {
        return renderer.Render(new Resolved2DPlan(new Resolved2DViewport(width, height), operations));
    }
}
