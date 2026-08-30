using Machina.Core.Styling;

namespace Oblivion.Product;

public sealed record OblivionMarkdownReadingStyle(
    ColorToken Surface,
    ColorToken Foreground,
    ColorToken MutedForeground,
    ColorToken HeadingForeground,
    ColorToken LinkForeground,
    ColorToken CodeSurface,
    ColorToken CodeForeground,
    ColorToken Border,
    ColorToken ScrollbarTrack,
    ColorToken ScrollbarThumb,
    ColorToken SourceSurface,
    ColorToken SourceForeground,
    ColorToken SourceBorder,
    double BodyLineHeight,
    double BodyLineGap,
    double SourceLineHeight,
    double SourceLineGap)
{
    public static OblivionMarkdownReadingStyle Default { get; } = new(
        Surface: ColorToken.Hex(0x0B1220FF),
        Foreground: ColorToken.Hex(0xE5EEF9FF),
        MutedForeground: ColorToken.Hex(0xB7C6D9FF),
        HeadingForeground: ColorToken.Hex(0xF8FBFFFF),
        LinkForeground: ColorToken.Hex(0x93C5FDFF),
        CodeSurface: ColorToken.Hex(0x111827FF),
        CodeForeground: ColorToken.Hex(0xE2E8F0FF),
        Border: ColorToken.Hex(0x334155FF),
        ScrollbarTrack: ColorToken.Hex(0x172033FF),
        ScrollbarThumb: ColorToken.Hex(0x64748BFF),
        SourceSurface: ColorToken.Hex(0x0F172AFF),
        SourceForeground: ColorToken.Hex(0xE2E8F0FF),
        SourceBorder: ColorToken.Hex(0x475569FF),
        BodyLineHeight: 18,
        BodyLineGap: 6,
        SourceLineHeight: 16,
        SourceLineGap: 4);
}
