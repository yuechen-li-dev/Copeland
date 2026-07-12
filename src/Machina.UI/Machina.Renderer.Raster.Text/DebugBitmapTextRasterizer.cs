using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Text;

public sealed class DebugBitmapTextRasterizer : ITextRasterizer
{
    private readonly ReadableBitmapTextRasterizer readable = new();

    public void DrawText(RasterSurface surface, Rect rect, string text, TextStyle style, Rgba32 color, Rect? clip = null)
    {
        readable.DrawText(surface, rect, text, style, color, clip);
    }
}
