namespace Machina.Core.Styling;

public enum TextSize
{
    Sm,
    Md,
    H1,
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
