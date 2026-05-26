using Xunit;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Encoding;
using Machina.Renderer.Raster.Rasterization;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Tests;

public sealed class RasterizerTests
{
    [Fact]
    public void NewSurface_StartsTransparent()
    {
        var surface = new RasterSurface(2, 2);

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                Assert.Equal(Rgba32.Transparent, surface.GetPixel(x, y));
            }
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void InvalidSurfaceSize_IsRejected(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterSurface(width, height));
    }

    [Fact]
    public void Clear_FillsAllPixels()
    {
        var surface = new RasterSurface(3, 2);
        var red = new Rgba32(255, 0, 0, 255);

        Rasterizer.Clear(surface, red);

        foreach (var pixel in surface.Pixels)
        {
            Assert.Equal(red, pixel);
        }
    }

    [Fact]
    public void FillRect_FillsExpectedIntegerPixels()
    {
        var surface = new RasterSurface(5, 5);
        var fill = new Rgba32(0, 255, 0, 255);

        Rasterizer.FillRect(surface, new Rect(1, 1, 2, 3), fill);

        AssertPixelRegion(surface, 1, 1, 3, 4, fill);
        AssertOutsideRegionIs(surface, 1, 1, 3, 4, Rgba32.Transparent);
    }

    [Fact]
    public void FillRect_ClipsLeftTop()
    {
        var surface = new RasterSurface(4, 4);
        var fill = new Rgba32(1, 2, 3, 255);

        Rasterizer.FillRect(surface, new Rect(-1, -1, 3, 3), fill);

        AssertPixelRegion(surface, 0, 0, 2, 2, fill);
        AssertOutsideRegionIs(surface, 0, 0, 2, 2, Rgba32.Transparent);
    }

    [Fact]
    public void FillRect_ClipsRightBottom()
    {
        var surface = new RasterSurface(4, 4);
        var fill = new Rgba32(1, 2, 3, 255);

        Rasterizer.FillRect(surface, new Rect(2, 2, 5, 5), fill);

        AssertPixelRegion(surface, 2, 2, 4, 4, fill);
        AssertOutsideRegionIs(surface, 2, 2, 4, 4, Rgba32.Transparent);
    }

    [Fact]
    public void FillRect_WithClip_IntersectsDrawArea()
    {
        var surface = new RasterSurface(5, 5);
        var fill = new Rgba32(7, 8, 9, 255);

        Rasterizer.FillRect(surface, new Rect(0, 0, 5, 5), fill, new Rect(1, 1, 2, 2));

        AssertPixelRegion(surface, 1, 1, 3, 3, fill);
        AssertOutsideRegionIs(surface, 1, 1, 3, 3, Rgba32.Transparent);
    }

    [Fact]
    public void FillRect_ZeroOrNegativeSize_DoesNothing()
    {
        var surface = new RasterSurface(3, 3);

        Rasterizer.FillRect(surface, new Rect(0, 0, 0, 10), new Rgba32(255, 0, 0, 255));
        Rasterizer.FillRect(surface, new Rect(0, 0, -1, 10), new Rgba32(255, 0, 0, 255));

        foreach (var pixel in surface.Pixels)
        {
            Assert.Equal(Rgba32.Transparent, pixel);
        }
    }

    [Theory]
    [InlineData(double.NaN, 0, 1, 1)]
    [InlineData(0, double.PositiveInfinity, 1, 1)]
    [InlineData(0, 0, double.NegativeInfinity, 1)]
    [InlineData(0, 0, 1, double.NaN)]
    public void FillRect_InvalidNumbers_AreRejected(double x, double y, double width, double height)
    {
        var surface = new RasterSurface(2, 2);

        Assert.Throws<ArgumentException>(() => Rasterizer.FillRect(surface, new Rect(x, y, width, height), Rgba32.White));
    }

    [Fact]
    public void Alpha255_ReplacesDestination()
    {
        var surface = new RasterSurface(1, 1);
        Rasterizer.Clear(surface, Rgba32.Black);

        Rasterizer.FillRect(surface, new Rect(0, 0, 1, 1), new Rgba32(255, 0, 0, 255));

        Assert.Equal(new Rgba32(255, 0, 0, 255), surface.GetPixel(0, 0));
    }

    [Fact]
    public void Alpha0_IsNoOp()
    {
        var surface = new RasterSurface(1, 1);
        var blue = new Rgba32(0, 0, 255, 255);
        Rasterizer.Clear(surface, blue);

        Rasterizer.FillRect(surface, new Rect(0, 0, 1, 1), new Rgba32(255, 0, 0, 0));

        Assert.Equal(blue, surface.GetPixel(0, 0));
    }

    [Fact]
    public void Alpha128_BlendsDeterministically_OverOpaqueBlack()
    {
        var surface = new RasterSurface(1, 1);
        Rasterizer.Clear(surface, Rgba32.Black);

        Rasterizer.FillRect(surface, new Rect(0, 0, 1, 1), new Rgba32(255, 0, 0, 128));

        Assert.Equal(new Rgba32(128, 0, 0, 255), surface.GetPixel(0, 0));
    }

    [Fact]
    public void PartialAlpha_OverTransparent_ProducesExpectedNonPremultipliedColor()
    {
        var surface = new RasterSurface(1, 1);
        Rasterizer.Clear(surface, Rgba32.Transparent);

        Rasterizer.FillRect(surface, new Rect(0, 0, 1, 1), new Rgba32(255, 0, 0, 128));

        Assert.Equal(new Rgba32(255, 0, 0, 128), surface.GetPixel(0, 0));
    }

    [Fact]
    public void StrokeRect_DrawsEdges()
    {
        var surface = new RasterSurface(5, 5);
        var color = new Rgba32(255, 0, 0, 255);
        Rasterizer.StrokeRect(surface, new Rect(1, 1, 3, 3), color, 1);

        Assert.Equal(color, surface.GetPixel(1, 1));
        Assert.Equal(color, surface.GetPixel(2, 1));
        Assert.Equal(color, surface.GetPixel(3, 1));
        Assert.Equal(color, surface.GetPixel(1, 3));
        Assert.Equal(color, surface.GetPixel(2, 3));
        Assert.Equal(color, surface.GetPixel(3, 3));
        Assert.Equal(color, surface.GetPixel(1, 2));
        Assert.Equal(color, surface.GetPixel(3, 2));
        Assert.Equal(Rgba32.Transparent, surface.GetPixel(2, 2));
    }

    [Fact]
    public void PpmWriter_EmitsValidHeaderAndPayload()
    {
        var surface = new RasterSurface(2, 1);
        surface.SetPixel(0, 0, new Rgba32(255, 0, 0, 255));
        surface.SetPixel(1, 0, new Rgba32(0, 255, 0, 255));

        var ppm = PpmWriter.WriteP6(surface);

        var expectedHeader = "P6\n2 1\n255\n";
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(expectedHeader);

        Assert.StartsWith(expectedHeader, System.Text.Encoding.ASCII.GetString(ppm, 0, headerBytes.Length));
        Assert.Equal(headerBytes.Length + 6, ppm.Length);

        var payload = ppm.Skip(headerBytes.Length).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 0, 255, 0 }, payload);
    }

    [Fact]
    public void PpmWriter_IgnoresAlphaAndWritesStoredRgb()
    {
        var surface = new RasterSurface(1, 1);
        surface.SetPixel(0, 0, new Rgba32(255, 0, 0, 128));

        var ppm = PpmWriter.WriteP6(surface);
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("P6\n1 1\n255\n");
        var payload = ppm.Skip(headerBytes.Length).ToArray();

        Assert.Equal(new byte[] { 255, 0, 0 }, payload);
    }

    [Fact]
    public void Rgba32_PackingRoundTripsUsingRrggbbaa()
    {
        var packed = 0xFF0000FFu;

        var color = Rgba32.FromRgba(packed);

        Assert.Equal(new Rgba32(255, 0, 0, 255), color);
        Assert.Equal(packed, color.ToRgba());
    }

    private static void AssertPixelRegion(RasterSurface surface, int left, int top, int rightExclusive, int bottomExclusive, Rgba32 color)
    {
        for (var y = top; y < bottomExclusive; y++)
        {
            for (var x = left; x < rightExclusive; x++)
            {
                Assert.Equal(color, surface.GetPixel(x, y));
            }
        }
    }

    private static void AssertOutsideRegionIs(RasterSurface surface, int left, int top, int rightExclusive, int bottomExclusive, Rgba32 expected)
    {
        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                var isInside = x >= left && x < rightExclusive && y >= top && y < bottomExclusive;
                if (isInside)
                {
                    continue;
                }

                Assert.Equal(expected, surface.GetPixel(x, y));
            }
        }
    }
}
