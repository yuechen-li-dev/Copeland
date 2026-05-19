using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Rasterization;
using Machina.Renderer.Raster.Surface;

namespace Machina.Renderer.Raster.Text;

public sealed class DebugBitmapTextRasterizer : ITextRasterizer
{
    private const int GlyphGap = 1;

    public void DrawText(RasterSurface surface, Rect rect, string text, TextStyle style, Rgba32 color, Rect? clip = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);

        if (text.Length == 0)
        {
            return;
        }

        var (glyphWidth, glyphHeight) = GetGlyphCellSize(style.Size);
        var drawX = (int)Math.Floor(rect.X);
        var drawY = (int)Math.Floor(rect.Y);
        var rectRight = rect.X + rect.Width;

        foreach (var ch in text)
        {
            if (drawX >= rectRight)
            {
                break;
            }

            if (!char.IsWhiteSpace(ch))
            {
                Rasterizer.FillRect(surface, new Rect(drawX, drawY, glyphWidth, glyphHeight), color, clip);
            }

            drawX += glyphWidth + GlyphGap;
        }
    }

    private static (int Width, int Height) GetGlyphCellSize(TextSize size)
    {
        return size switch
        {
            TextSize.Sm => (5, 8),
            TextSize.Md => (6, 10),
            TextSize.H1 => (10, 16),
            _ => (6, 10),
        };
    }
}
