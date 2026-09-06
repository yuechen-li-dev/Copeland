using Machina.Layout.Geometry;
using Xunit;

namespace Machina.Presentation.Tests;

public sealed class MachinaNineSliceTests
{
    [Theory]
    [InlineData(40, 40, 9)]
    [InlineData(240, 40, 9)]
    [InlineData(40, 240, 9)]
    [InlineData(10, 10, 4)]
    public void StretchCoversExactWideTallAndCollapsedCenterPanels(
        double width,
        double height,
        int expectedQuadCount)
    {
        MachinaNineSlicePrimitive primitive = Create(
            width,
            height,
            MachinaNineSliceMode.Stretch,
            MachinaNineSliceMode.Stretch);

        IReadOnlyList<MachinaNineSliceQuad> quads = MachinaNineSliceLowerer.Lower(primitive);

        Assert.Equal(expectedQuadCount, quads.Count);
        Assert.All(quads, quad =>
        {
            Assert.True(quad.DestinationRect.Width > 0);
            Assert.True(quad.DestinationRect.Height > 0);
        });
        Assert.Equal(width, quads.Max(quad => quad.DestinationRect.X + quad.DestinationRect.Width), 10);
        Assert.Equal(height, quads.Max(quad => quad.DestinationRect.Y + quad.DestinationRect.Height), 10);
    }

    [Fact]
    public void BorderScaleShrinksDestinationWithoutRecuttingSourceCorner()
    {
        MachinaNineSlicePrimitive primitive = Create(
            100,
            100,
            MachinaNineSliceMode.Stretch,
            MachinaNineSliceMode.Stretch,
            borderScale: 0.5);

        MachinaNineSliceQuad topLeft = MachinaNineSliceLowerer.Lower(primitive)[0];

        Assert.Equal(new Rect(0, 0, 5, 5), topLeft.DestinationRect);
        Assert.Equal(new Rect(0, 0, 10, 10), topLeft.SourceRect);
    }

    [Fact]
    public void TilingCropsFinalHorizontalAndVerticalTilesWithoutGaps()
    {
        MachinaNineSlicePrimitive primitive = Create(
            57,
            51,
            MachinaNineSliceMode.Tile,
            MachinaNineSliceMode.Tile);

        IReadOnlyList<MachinaNineSliceQuad> quads = MachinaNineSliceLowerer.Lower(primitive);

        Assert.True(quads.Count > 9);
        Assert.Contains(quads, quad => quad.SourceRect.Width is > 10 and < 20);
        Assert.Contains(quads, quad => quad.SourceRect.Height is > 10 and < 20);
        Assert.Equal(57, quads.Max(quad => quad.DestinationRect.X + quad.DestinationRect.Width), 10);
        Assert.Equal(51, quads.Max(quad => quad.DestinationRect.Y + quad.DestinationRect.Height), 10);
        Assert.All(quads, quad =>
        {
            Assert.InRange(quad.SourceRect.X, 0, 40);
            Assert.InRange(quad.SourceRect.Y, 0, 40);
            Assert.True(quad.SourceRect.X + quad.SourceRect.Width <= 40);
            Assert.True(quad.SourceRect.Y + quad.SourceRect.Height <= 40);
        });
    }

    [Fact]
    public void InvalidMarginsAndBorderScaleFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new MachinaNineSlicePrimitive(
            "test.invalid-margins",
            new MachinaTextureAssetId("test.atlas"),
            new Rect(0, 0, 40, 40),
            new Rect(0, 0, 100, 100),
            new MachinaSliceMargins(21, 10, 21, 10),
            MachinaNineSliceMode.Stretch,
            MachinaNineSliceMode.Stretch));

        Assert.Throws<ArgumentOutOfRangeException>(() => new MachinaNineSlicePrimitive(
            "test.invalid-scale",
            new MachinaTextureAssetId("test.atlas"),
            new Rect(0, 0, 40, 40),
            new Rect(0, 0, 100, 100),
            new MachinaSliceMargins(10, 10, 10, 10),
            MachinaNineSliceMode.Stretch,
            MachinaNineSliceMode.Stretch,
            borderScale: 0));
    }

    private static MachinaNineSlicePrimitive Create(
        double width,
        double height,
        MachinaNineSliceMode edgeMode,
        MachinaNineSliceMode centerMode,
        double borderScale = 1)
    {
        return new MachinaNineSlicePrimitive(
            "test.panel",
            new MachinaTextureAssetId("test.atlas"),
            new Rect(0, 0, 40, 40),
            new Rect(0, 0, width, height),
            new MachinaSliceMargins(10, 10, 10, 10),
            edgeMode,
            centerMode,
            borderScale);
    }
}
