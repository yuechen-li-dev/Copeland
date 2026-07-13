namespace Aurelian.Rendering.Contracts.Resolved2D;

/// <summary>
/// Straight-alpha RGBA color in byte channel order.
/// </summary>
public readonly record struct Resolved2DRgbaColor(byte R, byte G, byte B, byte A)
{
    public static Resolved2DRgbaColor Transparent { get; } = new(0, 0, 0, 0);

    public static Resolved2DRgbaColor White { get; } = new(255, 255, 255, 255);

    public static Resolved2DRgbaColor Black { get; } = new(0, 0, 0, 255);
}
