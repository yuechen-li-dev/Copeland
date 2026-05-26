using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class StandardButton
{
    public static UiNode Create(
        string text,
        NodeId? id = null,
        UiAction? action = null,
        ButtonVariant variant = ButtonVariant.Default,
        ButtonSize size = ButtonSize.Medium,
        bool disabled = false,
        StandardTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var style = CreateStyle(variant, effectiveTheme);
        var labelColor = style.Foreground ?? effectiveTheme.Colors.Foreground;
        var labelStyle = CreateLabelStyle(size, labelColor);

        var labelNode = UI.Anchor(
            child: UI.Text(
                text,
                id: CreateChildId(id, "label"),
                color: labelStyle.Color,
                size: labelStyle.Size,
                alignX: TextAlignX.Center,
                alignY: TextAlignY.Center),
            id: CreateChildId(id, "label-region"),
            left: 0,
            right: 0,
            top: 0,
            bottom: 0);

        var buttonWidth = ResolveButtonWidth(text, size);
        var buttonHeight = ResolveButtonHeight(size);

        return UI.Rect(
            child: labelNode,
            id: id,
            width: buttonWidth,
            height: buttonHeight,
            style: style) with
        {
            Semantics = new UiSemantics(
                UiRole.Button,
                text,
                Disabled: disabled,
                Focusable: !disabled),
            DeclaredAction = disabled ? null : action,
        };
    }

    public static UiStyle CreateStyle(
        ButtonVariant variant,
        StandardTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var colors = ResolveVariantColors(variant, theme);
        ColorToken? borderColor = variant == ButtonVariant.Outline ? theme.Colors.Border : null;
        var borderThickness = variant == ButtonVariant.Outline ? 1 : 0;

        return new UiStyle(
            Background: colors.Background,
            Foreground: colors.Foreground,
            Padding: 0,
            BorderColor: borderColor,
            BorderThickness: borderThickness);
    }

    private static TextStyle CreateLabelStyle(
        ButtonSize size,
        ColorToken foreground)
    {
        var textColor = foreground;
        var textSize = ResolveTextSize(size);

        return new TextStyle(
            Color: textColor,
            Size: textSize,
            AlignX: TextAlignX.Center,
            AlignY: TextAlignY.Center);
    }

    private static TextSize ResolveTextSize(ButtonSize size)
    {
        return size switch
        {
            ButtonSize.Small => TextSize.Sm,
            ButtonSize.Medium => TextSize.Md,
            ButtonSize.Large => TextSize.Md,
            ButtonSize.Icon => TextSize.Sm,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
    }

    private static double ResolveButtonWidth(string text, ButtonSize size)
    {
        var horizontalPadding = size switch
        {
            ButtonSize.Small => 20,
            ButtonSize.Medium => 24,
            ButtonSize.Large => 32,
            ButtonSize.Icon => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };

        if (size == ButtonSize.Icon)
        {
            return ResolveButtonHeight(size);
        }

        var estimatedTextWidth = Math.Max(1, text.Length) * 8;
        return estimatedTextWidth + horizontalPadding;
    }

    private static double ResolveButtonHeight(ButtonSize size)
    {
        return size switch
        {
            ButtonSize.Small => 28,
            ButtonSize.Medium => 32,
            ButtonSize.Large => 36,
            ButtonSize.Icon => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
        };
    }

    private static ButtonColors ResolveVariantColors(
        ButtonVariant variant,
        StandardTheme theme)
    {
        var colors = theme.Colors;

        return variant switch
        {
            ButtonVariant.Default => new ButtonColors(colors.Primary, colors.PrimaryForeground),
            ButtonVariant.Destructive => new ButtonColors(colors.Destructive, colors.DestructiveForeground),
            ButtonVariant.Outline => new ButtonColors(colors.Background, colors.Foreground),
            ButtonVariant.Secondary => new ButtonColors(colors.Secondary, colors.SecondaryForeground),
            ButtonVariant.Ghost => new ButtonColors(null, colors.Foreground),
            ButtonVariant.Link => new ButtonColors(null, colors.Primary),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };
    }

    private static NodeId? CreateChildId(NodeId? id, string suffix)
    {
        if (id is not { } value)
        {
            return null;
        }

        return new NodeId($"{value.Value}.{suffix}");
    }

    private sealed record ButtonColors(ColorToken? Background, ColorToken Foreground);
}
