using Machina.Core.Styling;

namespace Machina.Standard.Theme;

public sealed record StandardColors(
    ColorToken Background,
    ColorToken Foreground,
    ColorToken Primary,
    ColorToken PrimaryForeground,
    ColorToken Secondary,
    ColorToken SecondaryForeground,
    ColorToken Destructive,
    ColorToken DestructiveForeground,
    ColorToken Muted,
    ColorToken MutedForeground,
    ColorToken Border,
    ColorToken Accent,
    ColorToken AccentForeground);
