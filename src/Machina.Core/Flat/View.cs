using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Core.Styling;

namespace Machina.Core.Flat;

public static class View
{
    public static UiView Rect(
        ColorToken? background = null,
        ColorToken? foreground = null,
        ColorToken? borderColor = null,
        double borderThickness = 0,
        UiRole role = UiRole.Container,
        string? label = null,
        UiAction? action = null)
    {
        var style = new UiStyle(
            Background: background,
            Foreground: foreground,
            BorderColor: borderColor,
            BorderThickness: borderThickness);

        var semantics = new UiSemantics(role, label);
        return new UiView(Style: style, Semantics: semantics, Action: action);
    }

    public static UiView Text(
        string text,
        ColorToken? color = null,
        TextSize size = TextSize.Md,
        TextAlignX alignX = TextAlignX.Left,
        TextAlignY alignY = TextAlignY.Top,
        UiRole role = UiRole.Text)
    {
        var textStyle = new TextStyle(Color: color, Size: size, AlignX: alignX, AlignY: alignY);
        var semantics = new UiSemantics(role, text);
        return new UiView(TextStyle: textStyle, Semantics: semantics);
    }
}
