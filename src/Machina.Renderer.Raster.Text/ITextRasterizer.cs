using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Text;

public interface ITextRasterizer
{
    void DrawText(
        RasterSurface surface,
        Rect rect,
        string text,
        TextStyle style,
        Rgba32 color,
        Rect? clip = null);
}
