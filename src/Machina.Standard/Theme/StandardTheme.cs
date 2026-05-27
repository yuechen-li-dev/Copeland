using Machina.Core.Styling;

namespace Machina.Standard.Theme;

public sealed record StandardTheme(
    StandardColors Colors,
    StandardSpacing Spacing,
    StandardRadius Radius,
    StandardButtonStyles Button,
    StandardCardStyles Card,
    StandardInputStyles Input,
    StandardCheckboxStyles Checkbox,
    StandardSwitchStyles Switch)
{
    public static StandardTheme Default { get; } = CreateDefault();

    private static StandardTheme CreateDefault()
    {
        var colors = new StandardColors(
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
            AccentForeground: ColorToken.Hex(0x18181BFF));

        var spacing = new StandardSpacing(Xs: 4, Sm: 8, Md: 12, Lg: 16, Xl: 24);
        var radius = new StandardRadius(Sm: 4, Md: 6, Lg: 8);

        return new StandardTheme(
            Colors: colors,
            Spacing: spacing,
            Radius: radius,
            Button: new StandardButtonStyles(
                Default: new StandardButtonStyle(colors.Primary, colors.PrimaryForeground, null, 0, new TextStyle(colors.PrimaryForeground, TextSize.Md, TextAlignX.Center, TextAlignY.Center), 112, 32),
                Destructive: new StandardButtonStyle(colors.Destructive, colors.DestructiveForeground, null, 0, new TextStyle(colors.DestructiveForeground, TextSize.Md, TextAlignX.Center, TextAlignY.Center), 112, 32),
                Outline: new StandardButtonStyle(colors.Background, colors.Foreground, colors.Border, 1, new TextStyle(colors.Foreground, TextSize.Md, TextAlignX.Center, TextAlignY.Center), 112, 32),
                Secondary: new StandardButtonStyle(colors.Secondary, colors.SecondaryForeground, null, 0, new TextStyle(colors.SecondaryForeground, TextSize.Md, TextAlignX.Center, TextAlignY.Center), 112, 32),
                Ghost: new StandardButtonStyle(null, colors.Foreground, null, 0, new TextStyle(colors.Foreground, TextSize.Md, TextAlignX.Center, TextAlignY.Center), 112, 32),
                Link: new StandardButtonStyle(null, colors.Primary, null, 0, new TextStyle(colors.Primary, TextSize.Md, TextAlignX.Center, TextAlignY.Center), 112, 32)),
            Card: new StandardCardStyles(new StandardCardStyle(colors.Background, colors.Foreground, colors.Border, 1, spacing.Sm)),
            Input: new StandardInputStyles(new StandardInputStyle(colors.Background, colors.Foreground, colors.Border, 1, 180, 36, spacing.Sm, new TextStyle(colors.Foreground, TextSize.Md, TextAlignX.Left, TextAlignY.Center), new TextStyle(colors.MutedForeground, TextSize.Md, TextAlignX.Left, TextAlignY.Center), colors.Muted, colors.MutedForeground)),
            Checkbox: new StandardCheckboxStyles(new StandardCheckboxStyle(colors.Background, colors.Foreground, 1, colors.PrimaryForeground, colors.Foreground, colors.Muted, colors.MutedForeground, colors.MutedForeground, colors.MutedForeground, 18, 10, spacing.Sm, new TextStyle(colors.Foreground, TextSize.Sm, TextAlignX.Left, TextAlignY.Center))),
            Switch: new StandardSwitchStyles(new StandardSwitchStyle(colors.Muted, colors.Primary, colors.Border, 1, colors.Background, colors.Border, 1, colors.Foreground, colors.Muted, colors.MutedForeground, colors.Muted, colors.MutedForeground, colors.MutedForeground, 42, 20, 16, 2, spacing.Sm, new TextStyle(colors.Foreground, TextSize.Sm, TextAlignX.Left, TextAlignY.Center))));
    }
}
