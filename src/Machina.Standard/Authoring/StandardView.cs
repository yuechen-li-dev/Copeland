using Machina.Core.Actions;
using Machina.Core.Flat;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Standard.Theme;

namespace Machina.Standard.Authoring;

public static class StandardView
{
    public static UiView Card()
    {
        var colors = StandardTheme.Default.Colors;
        return new UiView(
            Style: new UiStyle(
                Background: colors.Background,
                BorderColor: colors.Border,
                BorderThickness: 1,
                Padding: 16),
            Semantics: new UiSemantics(UiRole.Container));
    }

    public static UiView Button(string label, UiAction? action = null)
    {
        var colors = StandardTheme.Default.Colors;
        return new UiView(
            Style: new UiStyle(
                Background: colors.Primary,
                Foreground: colors.PrimaryForeground,
                BorderColor: colors.Border,
                BorderThickness: 1,
                Padding: 12),
            TextStyle: new TextStyle(Color: colors.PrimaryForeground, Size: TextSize.Md),
            Semantics: new UiSemantics(UiRole.Button, label, Focusable: true),
            Action: action);
    }

    public static UiView Checkbox(string label, bool isChecked, UiAction? action = null)
    {
        var colors = StandardTheme.Default.Colors;
        var stateLabel = label + (isChecked ? " [x]" : " [ ]");
        return new UiView(
            Style: new UiStyle(Background: colors.Muted, Foreground: colors.Foreground, Padding: 8),
            TextStyle: new TextStyle(Color: colors.Foreground, Size: TextSize.Sm),
            Semantics: new UiSemantics(UiRole.Checkbox, stateLabel, Focusable: true),
            Action: action);
    }

    public static UiView Switch(string label, bool isOn, UiAction? action = null)
    {
        var colors = StandardTheme.Default.Colors;
        var background = isOn ? colors.Primary : colors.Muted;
        var stateLabel = label + (isOn ? " on" : " off");

        return new UiView(
            Style: new UiStyle(Background: background, Foreground: colors.Foreground, Padding: 8),
            TextStyle: new TextStyle(Color: colors.Foreground, Size: TextSize.Sm),
            Semantics: new UiSemantics(UiRole.Switch, stateLabel, Focusable: true),
            Action: action);
    }

    public static UiView Text(string text, TextSize size = TextSize.Md, ColorToken? color = null)
    {
        var colors = StandardTheme.Default.Colors;
        return View.Text(text, color ?? colors.Foreground, size);
    }

    public static UiView Label(string text, TextSize size = TextSize.Sm, ColorToken? color = null)
    {
        var colors = StandardTheme.Default.Colors;
        return View.Text(text, color ?? colors.Foreground, size, UiRole.Label);
    }

    public static UiView Badge(string text)
    {
        var colors = StandardTheme.Default.Colors;
        return new UiView(
            Style: new UiStyle(
                Background: colors.Muted,
                Foreground: colors.Foreground,
                BorderColor: colors.Border,
                BorderThickness: 1,
                Padding: 6),
            TextStyle: new TextStyle(Color: colors.Foreground, Size: TextSize.Sm),
            Semantics: new UiSemantics(UiRole.Label, text));
    }

    public static UiView Separator()
    {
        var colors = StandardTheme.Default.Colors;
        return new UiView(
            Style: new UiStyle(
                Background: colors.Border),
            Semantics: new UiSemantics(UiRole.Container));
    }

    public static UiView Input(string value, string? label = null, UiAction? action = null)
    {
        var colors = StandardTheme.Default.Colors;
        return new UiView(
            Style: new UiStyle(
                Background: colors.Background,
                Foreground: colors.Foreground,
                BorderColor: colors.Border,
                BorderThickness: 1,
                Padding: 10),
            TextStyle: new TextStyle(Color: colors.Foreground, Size: TextSize.Sm),
            Semantics: new UiSemantics(UiRole.Input, label ?? value, Focusable: true),
            Action: action);
    }
}
