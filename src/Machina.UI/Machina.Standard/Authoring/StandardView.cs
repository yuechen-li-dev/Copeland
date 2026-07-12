using Machina.Core.Actions;
using Machina.Core.Flat;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Standard.Theme;

namespace Machina.Standard.Authoring;

public static class StandardView
{
    // StandardView is a lightweight UiView metadata surface for flat-row authoring.
    // Prefer StandardUI component helpers for ordinary app/component controls.
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
            TextStyle: new TextStyle(Color: colors.PrimaryForeground, Size: TextSize.Md, AlignX: TextAlignX.Center, AlignY: TextAlignY.Center),
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

    // Advanced helper for custom component authors composing checkbox internals manually.
    // Prefer StandardUI.Checkbox for ordinary app/component code.
    public static UiView CheckboxBox(bool isChecked, UiAction? action = null, bool disabled = false)
    {
        var background = disabled
            ? ColorToken.Hex(0xE4E4E7FF)
            : ColorToken.Hex(0xFFFFFFFF);
        var border = disabled
            ? ColorToken.Hex(0xA1A1AAFF)
            : ColorToken.Hex(0x71717AFF);
        var fill = isChecked
            ? (disabled ? ColorToken.Hex(0xA1A1AAFF) : ColorToken.Hex(0x27272AFF))
            : ColorToken.Hex(0x00000000);

        return new UiView(
            Style: new UiStyle(
                Background: background,
                Foreground: fill,
                BorderColor: border,
                BorderThickness: 1,
                Padding: 4),
            Semantics: new UiSemantics(UiRole.Checkbox, isChecked ? "Checkbox [x]" : "Checkbox [ ]", Focusable: !disabled, Disabled: disabled),
            Action: disabled ? null : action);
    }

    // Advanced helper for custom component authors composing switch internals manually.
    // Prefer StandardUI.Switch for ordinary app/component code.
    public static UiView SwitchTrack(bool isOn, UiAction? action = null, bool disabled = false)
    {
        var background = disabled
            ? ColorToken.Hex(0xD4D4D8FF)
            : (isOn ? ColorToken.Hex(0x3F3F46FF) : ColorToken.Hex(0xE4E4E7FF));
        var border = disabled
            ? ColorToken.Hex(0xA1A1AAFF)
            : ColorToken.Hex(0x71717AFF);

        return new UiView(
            Style: new UiStyle(
                Background: background,
                Foreground: ColorToken.Hex(0x00000000),
                BorderColor: border,
                BorderThickness: 1,
                Padding: 0),
            Semantics: new UiSemantics(UiRole.Switch, isOn ? "Switch on" : "Switch off", Focusable: !disabled, Disabled: disabled),
            Action: disabled ? null : action);
    }

    // Advanced helper for custom component authors composing switch internals manually.
    // Prefer StandardUI.Switch for ordinary app/component code.
    public static UiView SwitchThumb(bool isOn, bool disabled = false)
    {
        var background = disabled
            ? ColorToken.Hex(0xE4E4E7FF)
            : ColorToken.Hex(0xFFFFFFFF);
        var border = disabled
            ? ColorToken.Hex(0xA1A1AAFF)
            : ColorToken.Hex(0x71717AFF);

        return new UiView(
            Style: new UiStyle(
                Background: background,
                Foreground: ColorToken.Hex(0x00000000),
                BorderColor: border,
                BorderThickness: 1,
                Padding: 0),
            Semantics: new UiSemantics(UiRole.Container));
    }

    public static UiView Text(
        string text,
        TextSize size = TextSize.Md,
        ColorToken? color = null,
        TextAlignX alignX = TextAlignX.Left,
        TextAlignY alignY = TextAlignY.Top)
    {
        var colors = StandardTheme.Default.Colors;
        return View.Text(text, color ?? colors.Foreground, size, alignX, alignY);
    }

    public static UiView Label(string text, TextSize size = TextSize.Sm, ColorToken? color = null)
    {
        var colors = StandardTheme.Default.Colors;
        return View.Text(text, color ?? colors.Foreground, size, TextAlignX.Left, TextAlignY.Top, UiRole.Label);
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
            TextStyle: new TextStyle(Color: colors.Foreground, Size: TextSize.Sm, AlignX: TextAlignX.Center, AlignY: TextAlignY.Center),
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
