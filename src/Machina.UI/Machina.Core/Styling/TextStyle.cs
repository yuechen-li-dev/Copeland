namespace Machina.Core.Styling;

public enum TextSize
{
    Sm,
    Md,
    H1,
    // Appended so existing numeric values stay stable. Hosts that support only the first three may reject these.
    Xs,
    Lg,
    Xl,
    H2,
}

public enum TextAlignX
{
    Left,
    Center,
    Right,
}

public enum TextAlignY
{
    Top,
    Center,
    Bottom,
}

public sealed record TextStyle(
    ColorToken? Color = null,
    TextSize Size = TextSize.Md,
    TextAlignX AlignX = TextAlignX.Left,
    TextAlignY AlignY = TextAlignY.Top);
