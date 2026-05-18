namespace Machina.Core.Styling;

public readonly record struct ColorToken(uint Rgba)
{
    public static ColorToken Hex(uint rgba) => new(rgba);

    public static ColorToken White { get; } = Hex(0xFFFFFFFF);

    public static ColorToken Gray { get; } = Hex(0x808080FF);

    public static ColorToken Gold { get; } = Hex(0xD4AF37FF);

    public static ColorToken Disabled { get; } = Hex(0x666666FF);
}
