using Machina.Core.Styling;
using Machina.Standard.Theme;

namespace Machina.ComponentGallery.Sample;

public static class GalleryTheme
{
    public static StandardTheme CreateProbeTheme(StandardTheme baseTheme)
    {
        return baseTheme with
        {
            Colors = baseTheme.Colors with
            {
                Background = ColorToken.Hex(0xF8FAFCFF),
                Primary = ColorToken.Hex(0x0F766EFF),
                PrimaryForeground = ColorToken.Hex(0xF0FDFAFF),
                Secondary = ColorToken.Hex(0xDCFCE7FF),
                SecondaryForeground = ColorToken.Hex(0x14532DFF),
                Muted = ColorToken.Hex(0xE2E8F0FF),
                MutedForeground = ColorToken.Hex(0x475569FF),
                Border = ColorToken.Hex(0x94A3B8FF),
                Accent = ColorToken.Hex(0xCCFBF1FF),
                AccentForeground = ColorToken.Hex(0x134E4AFF),
            },
            Button = baseTheme.Button with
            {
                Default = baseTheme.Button.Default with
                {
                    Background = ColorToken.Hex(0x0F766EFF),
                    Foreground = ColorToken.Hex(0xF0FDFAFF),
                    Width = 136,
                },
            },
            Card = baseTheme.Card with
            {
                Default = baseTheme.Card.Default with
                {
                    Background = ColorToken.Hex(0xECFDF5FF),
                    BorderColor = ColorToken.Hex(0x5EEAD4FF),
                    ContentInset = 12,
                },
            },
            Checkbox = baseTheme.Checkbox with
            {
                Default = baseTheme.Checkbox.Default with
                {
                    MarkColor = ColorToken.Hex(0x0F766EFF),
                },
            },
        };
    }
}
