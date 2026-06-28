using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tooling;
using Xunit;

namespace Machina.Fonts.Tooling.Tests;

public sealed class LayerCompositionTests
{
    [Fact]
    public void LayerComposition_SortsByZIndex()
    {
        DiagnosticLayerComposition composition = new(
            4,
            4,
            Rgba32.Black,
            [
                CreateImageLayer("top", 30, true),
                CreateImageLayer("bottom", 10, true),
                CreateImageLayer("middle", 20, true),
            ]);

        IReadOnlyList<DiagnosticLayer> ordered = composition.GetOrderedLayers();

        Assert.Collection(
            ordered,
            layer => Assert.Equal("bottom", layer.Id),
            layer => Assert.Equal("middle", layer.Id),
            layer => Assert.Equal("top", layer.Id));
    }

    [Fact]
    public void LayerComposition_RespectsVisibleFalse()
    {
        RgbaImage visibleImage = CreateFilledImage(4, 4, new Rgba32(0, 255, 0, 255));
        RgbaImage hiddenImage = CreateFilledImage(4, 4, new Rgba32(255, 0, 0, 255));
        DiagnosticLayerComposition composition = new(
            4,
            4,
            Rgba32.Black,
            [
                new DiagnosticImageLayer("visible", "Visible", true, 1d, 10, visibleImage),
                new DiagnosticImageLayer("hidden", "Hidden", false, 1d, 20, hiddenImage),
            ]);

        RgbaImage result = LayerCompositor.Compose(composition);

        Assert.Equal(new Rgba32(0, 255, 0, 255), result.GetPixel(1, 1));
    }

    [Fact]
    public void LayerComposition_RejectsInvalidOpacity()
    {
        DiagnosticLayerComposition composition = new(
            4,
            4,
            Rgba32.Black,
            [
                new DiagnosticImageLayer("invalid", "Invalid", true, 1.5d, 10, CreateFilledImage(4, 4, Rgba32.White)),
            ]);

        Assert.Throws<InvalidOperationException>(() => composition.Validate());
    }

    [Fact]
    public void LayerComposition_RejectsDuplicateLayerIds()
    {
        DiagnosticLayerComposition composition = new(
            4,
            4,
            Rgba32.Black,
            [
                CreateImageLayer("duplicate", 10, true),
                CreateImageLayer("duplicate", 20, true),
            ]);

        Assert.Throws<InvalidOperationException>(() => composition.Validate());
    }

    private static DiagnosticImageLayer CreateImageLayer(string id, int zIndex, bool visible)
    {
        return new DiagnosticImageLayer(id, id, visible, 1d, zIndex, CreateFilledImage(4, 4, Rgba32.White));
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        return new RgbaImage(width, height, Enumerable.Repeat(color, width * height).ToArray());
    }
}
