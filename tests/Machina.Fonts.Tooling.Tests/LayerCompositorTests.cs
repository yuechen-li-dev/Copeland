using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class LayerCompositorTests
{
    [Fact]
    public void LayerCompositor_ComposesImageLayersWithOpacity()
    {
        DiagnosticLayerComposition composition = new(
            4,
            4,
            Rgba32.Black,
            [
                new DiagnosticImageLayer("base", "Base", true, 1d, 0, CreateFilledImage(4, 4, new Rgba32(100, 100, 100, 255))),
                new DiagnosticImageLayer("overlay", "Overlay", true, 0.5d, 10, CreateFilledImage(4, 4, new Rgba32(200, 0, 0, 255))),
            ]);

        RgbaImage result = LayerCompositor.Compose(composition);

        Assert.Equal(new Rgba32(150, 50, 50, 255), result.GetPixel(1, 1));
    }

    [Fact]
    public void LayerCompositor_DrawsGridLayer()
    {
        DiagnosticLayerComposition composition = new(
            16,
            16,
            Rgba32.Black,
            [
                new DiagnosticGridLayer("grid", "Grid", true, 1d, 10, 4, 8, false, new Rgba32(40, 40, 40, 255), new Rgba32(80, 80, 80, 255), Rgba32.White),
            ]);

        RgbaImage result = LayerCompositor.Compose(composition);

        Assert.Equal(new Rgba32(40, 40, 40, 255), result.GetPixel(4, 5));
        Assert.Equal(new Rgba32(80, 80, 80, 255), result.GetPixel(8, 5));
        Assert.Equal(new Rgba32(40, 40, 40, 255), result.GetPixel(5, 4));
    }

    [Fact]
    public void LayerCompositor_DrawsBaselineLayer()
    {
        DiagnosticLayerComposition composition = new(
            12,
            12,
            Rgba32.Black,
            [
                new DiagnosticBaselineLayer("baseline", "Baseline", true, 1d, 10, 7, new Rgba32(255, 64, 64, 255)),
            ]);

        RgbaImage result = LayerCompositor.Compose(composition);

        Assert.Equal(new Rgba32(255, 64, 64, 255), result.GetPixel(6, 7));
    }

    [Fact]
    public void LayerCompositor_DrawsBoundsLayer()
    {
        DiagnosticLayerComposition composition = new(
            16,
            16,
            Rgba32.Black,
            [
                new DiagnosticBoundsLayer(
                    "bounds",
                    "Bounds",
                    true,
                    1d,
                    10,
                    [
                        new DiagnosticBoundsItem("browser", "browser", new FontDiagnosticBounds(1, 1, 4, 4), new Rgba32(0, 220, 255, 255)),
                        new DiagnosticBoundsItem("direct", "direct", new FontDiagnosticBounds(6, 6, 10, 10), new Rgba32(96, 255, 96, 255)),
                    ]),
            ]);

        RgbaImage result = LayerCompositor.Compose(composition);

        Assert.Equal(new Rgba32(0, 220, 255, 255), result.GetPixel(1, 1));
        Assert.Equal(new Rgba32(96, 255, 96, 255), result.GetPixel(6, 6));
    }

    [Fact]
    public void LayerCompositor_DoesNotMutateSourceImages()
    {
        RgbaImage source = CreateFilledImage(8, 8, new Rgba32(12, 24, 36, 255));
        Rgba32[] originalPixels = source.Pixels.ToArray();
        DiagnosticLayerComposition composition = new(
            8,
            8,
            Rgba32.Black,
            [
                new DiagnosticImageLayer("source", "Source", true, 1d, 0, source),
                new DiagnosticGridLayer("grid", "Grid", true, 1d, 10, 4, 8, true, new Rgba32(40, 40, 40, 255), new Rgba32(80, 80, 80, 255), Rgba32.White),
            ]);

        _ = LayerCompositor.Compose(composition);

        Assert.Equal(originalPixels, source.Pixels);
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        return new RgbaImage(width, height, Enumerable.Repeat(color, width * height).ToArray());
    }
}
