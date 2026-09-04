namespace Machina.Core.Styling;

public sealed record UiStyle(
    ColorToken? Background = null,
    ColorToken? Foreground = null,
    double Padding = 0,
    ColorToken? BorderColor = null,
    double BorderThickness = 0,
    bool ClipToBounds = false,
    UiShapeKind? Shape = null,
    double CornerRadius = 0);

public enum UiShapeKind
{
    RoundedRect,
    Circle,
    Pill,
}
