using Machina.Core.Styling;

namespace Machina.Standard.Theme;

public sealed record StandardTheme(
    StandardColors Colors,
    StandardSpacing Spacing,
    StandardRadius Radius)
{
    public static StandardTheme Default { get; } = new(
        Colors: new StandardColors(
            Background: ColorToken.Hex(0xFFFFFFFF),
            Foreground: ColorToken.Hex(0x09090BFF),
            Primary: ColorToken.Hex(0x18181BFF),
            PrimaryForeground: ColorToken.Hex(0xFAFAFAFF),
            Secondary: ColorToken.Hex(0xF4F4F5FF),
            SecondaryForeground: ColorToken.Hex(0x18181BFF),
            Destructive: ColorToken.Hex(0xDC2626FF),
            DestructiveForeground: ColorToken.Hex(0xFEF2F2FF),
            Muted: ColorToken.Hex(0xF4F4F5FF),
            MutedForeground: ColorToken.Hex(0x71717AFF),
            Border: ColorToken.Hex(0xE4E4E7FF),
            Accent: ColorToken.Hex(0xF4F4F5FF),
            AccentForeground: ColorToken.Hex(0x18181BFF)),
        Spacing: new StandardSpacing(
            Xs: 4,
            Sm: 8,
            Md: 12,
            Lg: 16,
            Xl: 24),
        Radius: new StandardRadius(
            Sm: 4,
            Md: 6,
            Lg: 8));
}
