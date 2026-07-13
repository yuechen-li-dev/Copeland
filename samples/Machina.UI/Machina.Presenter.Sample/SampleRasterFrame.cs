using Aurelian.Rendering.Contracts.Resolved2D;
using AurelianRasterFrame = Aurelian.Rendering.Raster.RasterFrame;
using FontRgba32 = Machina.Fonts.ReferenceRendering.Rgba32;

namespace Machina.Presenter.Sample;

public readonly record struct SampleRgba32(byte R, byte G, byte B, byte A)
{
    public static SampleRgba32 FromRgba(uint rgba)
    {
        return new SampleRgba32(
            (byte)(rgba >> 24),
            (byte)(rgba >> 16),
            (byte)(rgba >> 8),
            (byte)rgba);
    }

    public static implicit operator Resolved2DRgbaColor(SampleRgba32 color) => new(color.R, color.G, color.B, color.A);

    public static implicit operator SampleRgba32(Resolved2DRgbaColor color) => new(color.R, color.G, color.B, color.A);

    public static implicit operator FontRgba32(SampleRgba32 color) => new(color.R, color.G, color.B, color.A);

    public static implicit operator SampleRgba32(FontRgba32 color) => new(color.R, color.G, color.B, color.A);
}

/// <summary>
/// Mutable sample staging surface used only to compose optional diagnostic overlays.
/// The production raster is completed by Aurelian before this staging step.
/// </summary>
public sealed class SampleRasterSurface
{
    public SampleRasterSurface(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new Resolved2DRgbaColor[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public Resolved2DRgbaColor[] Pixels { get; }

    public Resolved2DRgbaColor GetPixel(int x, int y) => Pixels[(y * Width) + x];

    public void SetPixel(int x, int y, Resolved2DRgbaColor color) => Pixels[(y * Width) + x] = color;

    public static SampleRasterSurface From(AurelianRasterFrame frame)
    {
        var surface = new SampleRasterSurface(frame.Surface.Width, frame.Surface.Height);
        Array.Copy(frame.Surface.CopyPixels(), surface.Pixels, surface.Pixels.Length);
        return surface;
    }
}

public sealed record SampleRasterFrame(SampleRasterSurface Surface)
{
    public int Width => Surface.Width;

    public int Height => Surface.Height;

    public static SampleRasterFrame From(AurelianRasterFrame frame) => new(SampleRasterSurface.From(frame));
}
