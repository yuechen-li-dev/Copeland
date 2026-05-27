using Machina.Core.Styling;
using Machina.Standard.Components;

namespace Machina.Standard.Theme;

public sealed record StandardButtonStyle(
    ColorToken? Background,
    ColorToken Foreground,
    ColorToken? BorderColor,
    double BorderThickness,
    TextStyle TextStyle,
    double Width,
    double Height);

public sealed record StandardButtonStyles(
    StandardButtonStyle Default,
    StandardButtonStyle Destructive,
    StandardButtonStyle Outline,
    StandardButtonStyle Secondary,
    StandardButtonStyle Ghost,
    StandardButtonStyle Link)
{
    public StandardButtonStyle ForVariant(ButtonVariant variant)
    {
        return variant switch
        {
            ButtonVariant.Default => Default,
            ButtonVariant.Destructive => Destructive,
            ButtonVariant.Outline => Outline,
            ButtonVariant.Secondary => Secondary,
            ButtonVariant.Ghost => Ghost,
            ButtonVariant.Link => Link,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };
    }
}

public sealed record StandardCardStyle(
    ColorToken Background,
    ColorToken Foreground,
    ColorToken BorderColor,
    double BorderThickness,
    double ContentInset);

public sealed record StandardCardStyles(StandardCardStyle Default);

public sealed record StandardInputStyle(
    ColorToken Background,
    ColorToken Foreground,
    ColorToken BorderColor,
    double BorderThickness,
    double Width,
    double Height,
    double ContentInset,
    TextStyle TextStyle,
    TextStyle PlaceholderTextStyle,
    ColorToken DisabledBackground,
    ColorToken DisabledForeground);

public sealed record StandardInputStyles(StandardInputStyle Default);

public sealed record StandardCheckboxStyle(
    ColorToken BoxBackground,
    ColorToken BoxBorderColor,
    double BoxBorderThickness,
    ColorToken MarkColor,
    ColorToken LabelColor,
    ColorToken DisabledBackground,
    ColorToken DisabledBorderColor,
    ColorToken DisabledMarkColor,
    ColorToken DisabledLabelColor,
    double BoxSize,
    double MarkSize,
    double Gap,
    TextStyle LabelTextStyle);

public sealed record StandardCheckboxStyles(StandardCheckboxStyle Default);

public sealed record StandardSwitchStyle(
    ColorToken TrackOffBackground,
    ColorToken TrackOnBackground,
    ColorToken TrackBorderColor,
    double TrackBorderThickness,
    ColorToken ThumbBackground,
    ColorToken ThumbBorderColor,
    double ThumbBorderThickness,
    ColorToken LabelColor,
    ColorToken DisabledTrackBackground,
    ColorToken DisabledTrackBorderColor,
    ColorToken DisabledThumbBackground,
    ColorToken DisabledThumbBorderColor,
    ColorToken DisabledLabelColor,
    double TrackWidth,
    double TrackHeight,
    double ThumbSize,
    double ThumbInset,
    double Gap,
    TextStyle LabelTextStyle);

public sealed record StandardSwitchStyles(StandardSwitchStyle Default);
