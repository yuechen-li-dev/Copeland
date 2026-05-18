namespace Machina.Core.Styling;

public sealed record Theme(
    UiStyle DefaultStyle,
    TextStyle DefaultTextStyle)
{
    public static Theme Empty { get; } = new(new UiStyle(), new TextStyle());
}
