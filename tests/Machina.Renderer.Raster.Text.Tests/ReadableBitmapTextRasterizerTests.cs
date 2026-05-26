using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Rasterization;
using Machina.Renderer.Raster.Surface;
using Machina.Renderer.Raster.Text;
using Xunit;

namespace Machina.Renderer.Raster.Text.Tests;

public sealed class ReadableBitmapTextRasterizerTests
{
    [Fact]
    public void DrawText_SingleGlyphA_HasRecognizablePattern()
    {
        var surface = new RasterSurface(20, 20);
        var rasterizer = new ReadableBitmapTextRasterizer();

        rasterizer.DrawText(surface, new Rect(0, 0, 20, 20), "A", new TextStyle(Size: TextSize.Sm), Rgba32.White);

        Assert.Equal(Rgba32.White, surface.GetPixel(2, 0));
        Assert.Equal(Rgba32.White, surface.GetPixel(2, 3));
        Assert.Equal(Rgba32.White, surface.GetPixel(0, 2));
        Assert.Equal(Rgba32.White, surface.GetPixel(4, 2));
        Assert.Equal(Rgba32.Transparent, surface.GetPixel(0, 0));
    }

    [Fact]
    public void DrawText_LowercaseMapsToUppercase()
    {
        var upper = RenderToSurface("A");
        var lower = RenderToSurface("a");

        Assert.Equal(upper.Pixels, lower.Pixels);
    }

    [Fact]
    public void DrawText_DigitRenders()
    {
        var surface = RenderToSurface("0");

        Assert.Equal(Rgba32.White, surface.GetPixel(1, 0));
        Assert.Equal(Rgba32.White, surface.GetPixel(0, 1));
        Assert.Equal(Rgba32.White, surface.GetPixel(4, 5));
    }

    [Fact]
    public void DrawText_SpaceAdvancesWithoutDrawing()
    {
        var surface = new RasterSurface(30, 10);
        var rasterizer = new ReadableBitmapTextRasterizer();

        rasterizer.DrawText(surface, new Rect(0, 0, 30, 10), "A A", new TextStyle(Size: TextSize.Sm), Rgba32.White);

        Assert.True(HasInkInRegion(surface, 0, 0, 5, 7));
        Assert.False(HasInkInRegion(surface, 6, 0, 5, 7));
        Assert.True(HasInkInRegion(surface, 12, 0, 5, 7));
    }

    [Fact]
    public void DrawText_TextSizeScaleAffectsPaintedBounds()
    {
        var rasterizer = new ReadableBitmapTextRasterizer();

        var smBounds = DrawAndBounds(rasterizer, TextSize.Sm);
        var mdBounds = DrawAndBounds(rasterizer, TextSize.Md);
        var h1Bounds = DrawAndBounds(rasterizer, TextSize.H1);

        Assert.Equal((5, 7), smBounds);
        Assert.Equal((10, 14), mdBounds);
        Assert.Equal((15, 21), h1Bounds);
    }

    [Fact]
    public void DrawText_ClipStillApplies()
    {
        var surface = new RasterSurface(60, 20);
        var rasterizer = new ReadableBitmapTextRasterizer();
        var clip = new Rect(0, 0, 10, 20);

        rasterizer.DrawText(surface, new Rect(0, 0, 60, 20), "HELLO", new TextStyle(), Rgba32.White, clip);

        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 10; x < surface.Width; x++)
            {
                Assert.Equal(Rgba32.Transparent, surface.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void DrawText_AlphaBlendingStillWorks()
    {
        var surface = new RasterSurface(20, 20);
        var rasterizer = new ReadableBitmapTextRasterizer();
        Rasterizer.Clear(surface, Rgba32.Black);

        rasterizer.DrawText(surface, new Rect(0, 0, 20, 20), "A", new TextStyle(Size: TextSize.Sm), new Rgba32(255, 0, 0, 128));

        Assert.Equal(new Rgba32(128, 0, 0, 255), surface.GetPixel(2, 0));
    }

    [Fact]
    public void DrawText_UnknownGlyphUsesFallback()
    {
        var surface = RenderToSurface("🙂");

        Assert.True(CountNonTransparentPixels(surface) > 0);
    }

    private static RasterSurface RenderToSurface(string text)
    {
        var surface = new RasterSurface(30, 20);
        var rasterizer = new ReadableBitmapTextRasterizer();
        rasterizer.DrawText(surface, new Rect(0, 0, 30, 20), text, new TextStyle(Size: TextSize.Sm), Rgba32.White);
        return surface;
    }

    private static bool HasInkInRegion(RasterSurface surface, int x, int y, int width, int height)
    {
        for (var py = y; py < y + height; py++)
        {
            for (var px = x; px < x + width; px++)
            {
                if (surface.GetPixel(px, py).A > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (int Width, int Height) DrawAndBounds(ReadableBitmapTextRasterizer rasterizer, TextSize size)
    {
        var surface = new RasterSurface(50, 50);
        rasterizer.DrawText(surface, new Rect(0, 0, 50, 50), "A", new TextStyle(Size: size), Rgba32.White);

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).A == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return (maxX - minX + 1, maxY - minY + 1);
    }

    private static int CountNonTransparentPixels(RasterSurface surface)
    {
        var count = 0;
        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).A > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
