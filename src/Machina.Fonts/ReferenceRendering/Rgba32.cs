namespace Machina.Fonts.ReferenceRendering;

public readonly record struct Rgba32(
    byte R,
    byte G,
    byte B,
    byte A)
{
    public static readonly Rgba32 Transparent = new(0, 0, 0, 0);
    public static readonly Rgba32 Black = new(0, 0, 0, 255);
    public static readonly Rgba32 White = new(255, 255, 255, 255);
}
