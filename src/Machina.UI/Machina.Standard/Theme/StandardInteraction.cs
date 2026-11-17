using Machina.Core.Styling;

namespace Machina.Standard.Theme;

/// <summary>Immediate theme-relative feedback; selected colors remain the base color.</summary>
public static class StandardInteraction
{
    public static ColorToken Surface(ColorToken background, ColorToken foreground, bool hovered, bool pressed)
    {
        if (!hovered && !pressed) return background;
        return Mix(background, foreground, pressed ? 0.22 : 0.11);
    }

    public static ColorToken Mix(ColorToken from, ColorToken toward, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        uint Channel(int shift)
        {
            double left = (from.Rgba >> shift) & 255;
            double right = (toward.Rgba >> shift) & 255;
            return (uint)Math.Round(left + (right - left) * amount);
        }
        return new((Channel(24) << 24) | (Channel(16) << 16) | (Channel(8) << 8) | (from.Rgba & 255));
    }
}
