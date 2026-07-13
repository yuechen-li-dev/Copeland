using Aurelian.Rendering.Contracts.Resolved2D;

namespace Aurelian.Rendering.Raster;

/// <summary>
/// Immutable straight-alpha RGBA pixel surface produced by the CPU renderer.
/// Pixels are row-major, with (0, 0) at the top-left.
/// </summary>
public sealed class RasterSurface
{
    private readonly Resolved2DRgbaColor[] pixels;

    internal RasterSurface(int width, int height, Resolved2DRgbaColor[] pixels)
    {
        Width = width;
        Height = height;
        this.pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public Resolved2DRgbaColor GetPixel(int x, int y)
    {
        return pixels[GetIndex(x, y)];
    }

    public IReadOnlyList<Resolved2DRgbaColor> Pixels => Array.AsReadOnly(pixels);

    public Resolved2DRgbaColor[] CopyPixels()
    {
        return (Resolved2DRgbaColor[])pixels.Clone();
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
