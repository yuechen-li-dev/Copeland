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
        return new UiView(
            Style: new UiStyle(Background: colors.Muted, Foreground: colors.Foreground, Padding: 8),
            TextStyle: new TextStyle(Color: colors.Foreground, Size: TextSize.Sm),
            Semantics: new UiSemantics(UiRole.Checkbox, label + (isChecked ? " [x]" : " [ ]"), Focusable: true),
            Action: action);
    }

    public static UiView Switch(string label, bool isOn, UiAction? action = null)
    {
        var colors = StandardTheme.Default.Colors;
        var background = isOn ? colors.Primary : colors.Muted;
        return new UiView(
            Style: new UiStyle(Background: background, Foreground: colors.Foreground, Padding: 8),
            TextStyle: new TextStyle(Color: colors.Foreground, Size: TextSize.Sm),
            Semantics: new UiSemantics(UiRole.Switch, label + (isOn ? " on" : " off"), Focusable: true),
            Action: action);
    }

    public static UiView Text(string text, TextSize size = TextSize.Md, ColorToken? color = null)
    {
        var colors = StandardTheme.Default.Colors;
        return View.Text(text, color ?? colors.Foreground, size);
    }
}
