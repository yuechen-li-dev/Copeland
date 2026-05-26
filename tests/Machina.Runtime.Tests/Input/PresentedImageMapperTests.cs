using Xunit;
using Machina.Runtime.Input;

namespace Machina.Runtime.Tests.Input;

public sealed class PresentedImageMapperTests
{
    [Fact]
    public void None_MapsDirectCoordinates()
    {
        PointerPoint? mapped = PresentedImageMapper.ToRootPoint(
            new PointerPoint(15, 25),
            100,
            50,
            new PresentedImageRect(10, 20, 100, 50),
            ImageStretchMode.None);

        Assert.NotNull(mapped);
        Assert.Equal(5, mapped.Value.X);
        Assert.Equal(5, mapped.Value.Y);
    }

    [Theory]
    [InlineData(9, 25)]
    [InlineData(110, 25)]
    [InlineData(15, 70)]
    public void None_OutsideBounds_ReturnsNull(double x, double y)
    {
        PointerPoint? mapped = PresentedImageMapper.ToRootPoint(
            new PointerPoint(x, y),
            100,
            50,
            new PresentedImageRect(10, 20, 100, 50),
            ImageStretchMode.None);

        Assert.Null(mapped);
    }

    [Fact]
    public void Fill_MapsScaledCoordinates()
    {
        PointerPoint? mapped = PresentedImageMapper.ToRootPoint(
            new PointerPoint(100, 50),
            100,
            50,
            new PresentedImageRect(0, 0, 200, 100),
            ImageStretchMode.Fill);

        Assert.NotNull(mapped);
        Assert.Equal(50, mapped.Value.X);
        Assert.Equal(25, mapped.Value.Y);
    }

    [Fact]
    public void Fill_MapsNonUniformScale()
    {
        PointerPoint? mapped = PresentedImageMapper.ToRootPoint(
            new PointerPoint(100, 200),
            100,
            100,
            new PresentedImageRect(0, 0, 200, 400),
            ImageStretchMode.Fill);

        Assert.NotNull(mapped);
        Assert.Equal(50, mapped.Value.X);
        Assert.Equal(50, mapped.Value.Y);
    }

    [Fact]
    public void Uniform_MapsLetterboxedCoordinates()
    {
        PointerPoint? mapped = PresentedImageMapper.ToRootPoint(
            new PointerPoint(100, 100),
            100,
            50,
            new PresentedImageRect(0, 0, 200, 200),
            ImageStretchMode.Uniform);

        Assert.NotNull(mapped);
        Assert.Equal(50, mapped.Value.X);
        Assert.Equal(25, mapped.Value.Y);
    }

    [Theory]
    [InlineData(100, 25)]
    [InlineData(100, 175)]
    public void Uniform_OutsideLetterbox_ReturnsNull(double x, double y)
    {
        PointerPoint? mapped = PresentedImageMapper.ToRootPoint(
            new PointerPoint(x, y),
            100,
            50,
            new PresentedImageRect(0, 0, 200, 200),
            ImageStretchMode.Uniform);

        Assert.Null(mapped);
    }

    [Theory]
    [InlineData(ImageStretchMode.Fill, 200, 100)]
    [InlineData(ImageStretchMode.Uniform, 200, 150)]
    public void HalfOpenBounds_RightBottomEdges_ReturnNull(ImageStretchMode mode, double x, double y)
    {
        PointerPoint? mapped = PresentedImageMapper.ToRootPoint(
            new PointerPoint(x, y),
            100,
            50,
            new PresentedImageRect(0, 0, 200, 200),
            mode);

        Assert.Null(mapped);
    }

    [Theory]
    [InlineData(0, 50, 0, 0, 100, 50)]
    [InlineData(100, 0, 0, 0, 100, 50)]
    [InlineData(double.NaN, 50, 0, 0, 100, 50)]
    [InlineData(100, double.PositiveInfinity, 0, 0, 100, 50)]
    [InlineData(100, 50, 0, 0, 0, 50)]
    [InlineData(100, 50, 0, 0, 100, 0)]
    [InlineData(100, 50, 0, 0, double.NaN, 50)]
    [InlineData(100, 50, 0, 0, 100, double.NegativeInfinity)]
    public void InvalidDimensions_Throw(
        double sourceWidth,
        double sourceHeight,
        double destinationX,
        double destinationY,
        double destinationWidth,
        double destinationHeight)
    {
        Assert.ThrowsAny<ArgumentException>(() => PresentedImageMapper.ToRootPoint(
            new PointerPoint(10, 10),
            sourceWidth,
            sourceHeight,
            new PresentedImageRect(destinationX, destinationY, destinationWidth, destinationHeight),
            ImageStretchMode.Fill));
    }
}
