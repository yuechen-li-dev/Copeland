using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Rasterization;
using Machina.Renderer.Raster.Surface;
using Machina.Renderer.Raster.Text;
using Xunit;

namespace Machina.Renderer.Raster.Text.Tests;

public sealed class DebugBitmapTextRasterizerTests
{
    [Fact]
    public void DrawText_EmptyText_IsNoOp()
    {
        var surface = new RasterSurface(20, 20);
        var rasterizer = new DebugBitmapTextRasterizer();

        rasterizer.DrawText(surface, new Rect(0, 0, 20, 20), string.Empty, new TextStyle(), Rgba32.White);

        AssertAllTransparent(surface);
    }

    [Fact]
    public void DrawText_SingleCharacter_DrawsExpectedCell()
    {
        var surface = new RasterSurface(20, 20);
        var rasterizer = new DebugBitmapTextRasterizer();

        rasterizer.DrawText(surface, new Rect(1, 2, 20, 20), "A", new TextStyle(Size: TextSize.Md), Rgba32.White);

        Assert.Equal(Rgba32.White, surface.GetPixel(1, 2));
        Assert.Equal(Rgba32.Transparent, surface.GetPixel(0, 0));
    }

    [Fact]
    public void DrawText_Space_AdvancesWithoutPainting()
    {
        var surface = new RasterSurface(20, 20);
        var rasterizer = new DebugBitmapTextRasterizer();

        rasterizer.DrawText(surface, new Rect(0, 0, 20, 20), " ", new TextStyle(), Rgba32.White);

        AssertAllTransparent(surface);
    }

    [Fact]
    public void DrawText_MultipleCharacters_UseSeparatedCells()
    {
        var surface = new RasterSurface(30, 20);
        var rasterizer = new DebugBitmapTextRasterizer();

        rasterizer.DrawText(surface, new Rect(0, 0, 30, 20), "AB", new TextStyle(Size: TextSize.Md), Rgba32.White);

        Assert.Equal(Rgba32.White, surface.GetPixel(0, 0));
        Assert.Equal(Rgba32.Transparent, surface.GetPixel(6, 0));
        Assert.Equal(Rgba32.White, surface.GetPixel(7, 0));
    }

    [Fact]
    public void DrawText_ClipsToSurfaceBounds()
    {
        var surface = new RasterSurface(6, 6);
        var rasterizer = new DebugBitmapTextRasterizer();

        rasterizer.DrawText(surface, new Rect(-2, -3, 10, 10), "A", new TextStyle(Size: TextSize.H1), Rgba32.White);

        Assert.Equal(Rgba32.White, surface.GetPixel(0, 0));
        Assert.Equal(Rgba32.White, surface.GetPixel(5, 5));
    }

    [Fact]
    public void DrawText_TextSizeChangesPaintedArea()
    {
        var rasterizer = new DebugBitmapTextRasterizer();

        var sm = DrawAndCount(rasterizer, TextSize.Sm);
        var md = DrawAndCount(rasterizer, TextSize.Md);
        var h1 = DrawAndCount(rasterizer, TextSize.H1);

        Assert.True(sm < md);
        Assert.True(md < h1);
    }

    [Fact]
    public void DrawText_UsesRasterizerBlending()
    {
        var surface = new RasterSurface(20, 20);
        var rasterizer = new DebugBitmapTextRasterizer();
        Rasterizer.Clear(surface, Rgba32.Black);

        rasterizer.DrawText(surface, new Rect(0, 0, 20, 20), "A", new TextStyle(), new Rgba32(255, 0, 0, 128));

        Assert.Equal(new Rgba32(128, 0, 0, 255), surface.GetPixel(0, 0));
    }

    private static int DrawAndCount(DebugBitmapTextRasterizer rasterizer, TextSize size)
    {
        var surface = new RasterSurface(32, 32);
        rasterizer.DrawText(surface, new Rect(0, 0, 32, 32), "A", new TextStyle(Size: size), Rgba32.White);
        return CountNonTransparentPixels(surface);
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

    private static void AssertAllTransparent(RasterSurface surface)
    {
        Assert.Equal(0, CountNonTransparentPixels(surface));
    }
}
