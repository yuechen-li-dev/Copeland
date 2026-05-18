namespace Machina.Core.Styling;

public enum TextSize
{
    Sm,
    Md,
    H1,
}

public sealed record TextStyle(
    ColorToken? Color = null,
    TextSize Size = TextSize.Md);
