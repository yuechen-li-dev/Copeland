namespace Machina.Renderer.Raster.Colors;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A)
{
    public static Rgba32 Transparent => new(0, 0, 0, 0);

    public static Rgba32 Black => new(0, 0, 0, 255);

    public static Rgba32 White => new(255, 255, 255, 255);

    public static Rgba32 FromRgba(uint rgba)
    {
        var red = (byte)((rgba >> 24) & 0xFF);
        var green = (byte)((rgba >> 16) & 0xFF);
        var blue = (byte)((rgba >> 8) & 0xFF);
        var alpha = (byte)(rgba & 0xFF);

        return new Rgba32(red, green, blue, alpha);
    }

    public uint ToRgba()
    {
        return ((uint)R << 24) |
               ((uint)G << 16) |
               ((uint)B << 8) |
               A;
    }
}
