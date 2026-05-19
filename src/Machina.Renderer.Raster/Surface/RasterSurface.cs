using Machina.Renderer.Raster.Colors;

namespace Machina.Renderer.Raster.Surface;

public sealed class RasterSurface
{
    public int Width { get; }

    public int Height { get; }

    public Rgba32[] Pixels { get; }

    public RasterSurface(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        Width = width;
        Height = height;
        Pixels = new Rgba32[width * height];
    }

    public Rgba32 GetPixel(int x, int y)
    {
        var index = GetIndex(x, y);
        return Pixels[index];
    }

    public void SetPixel(int x, int y, Rgba32 color)
    {
        var index = GetIndex(x, y);
        Pixels[index] = color;
    }

    private int GetIndex(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "x must be inside surface bounds.");
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "y must be inside surface bounds.");
        }

        return (y * Width) + x;
    }
}
