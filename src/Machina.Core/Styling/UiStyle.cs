namespace Machina.Core.Styling;

public sealed record UiStyle(
    ColorToken? Background = null,
    ColorToken? Foreground = null,
    double Padding = 0);
