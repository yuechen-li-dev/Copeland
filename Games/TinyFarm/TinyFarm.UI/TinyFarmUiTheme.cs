using Machina.Core.Styling;
using Machina.Standard.Theme;

namespace TinyFarm.Native;

/// <summary>TinyFarm's art direction; component construction belongs to Machina.Standard.</summary>
internal static class TinyFarmUiTheme
{
    public const uint Gold = 0xF5DDB5FF;
    public const uint Foreground = 0xE7EBDDFF;
    public const uint HudBackground = 0x19362BD9;
    public const uint MenuBackground = 0x19362BFC;

    public static ColorToken Text { get; } = ColorToken.Hex(Foreground);
    public static ColorToken Accent { get; } = ColorToken.Hex(Gold);

    private static readonly StandardButtonStyle NormalButton = new(
        ColorToken.Hex(0x29473FFF),
        ColorToken.Hex(Gold),
        ColorToken.Hex(0x83978480),
        1,
        new TextStyle(ColorToken.Hex(Gold), TextSize.Md, TextAlignX.Center, TextAlignY.Center),
        112,
        34);

    public static StandardButtonStyle Button(bool selected = false, bool disabled = false)
    {
        ColorToken foreground = ColorToken.Hex(Gold);
        if (disabled)
        {
            foreground = ColorToken.Hex(0x8A9B8EFF);
        }

        return NormalButton with
        {
            Background = ColorToken.Hex(selected ? 0x4A6655FFu : 0x29473FFFu),
            Foreground = foreground,
            TextStyle = NormalButton.TextStyle with { Color = foreground },
        };
    }

    public static StandardCardStyle Panel(uint background = HudBackground)
    {
        return new StandardCardStyle(
            ColorToken.Hex(background),
            ColorToken.Hex(Foreground),
            ColorToken.Hex(0x8DA48180),
            BorderThickness: 1,
            ContentInset: 0,
            CornerRadius: 12);
    }
}
