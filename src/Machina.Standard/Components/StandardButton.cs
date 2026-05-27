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
    public static UiNode Create(string text, NodeId? id = null, UiAction? action = null, ButtonVariant variant = ButtonVariant.Default, ButtonSize size = ButtonSize.Medium, bool disabled = false, StandardTheme? theme = null, StandardButtonStyle? style = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var variantStyle = effectiveTheme.Button.ForVariant(variant);
        var effectiveStyle = style ?? variantStyle;
        var labelTextStyle = ResolveLabelTextStyle(effectiveStyle, size, style is not null);

        var labelNode = UI.Anchor(
            child: UI.Text(
                text,
                id: CreateChildId(id, "label"),
                color: labelTextStyle.Color,
                size: labelTextStyle.Size,
                alignX: labelTextStyle.AlignX,
                alignY: labelTextStyle.AlignY,
                style: labelTextStyle),
            id: CreateChildId(id, "label-region"),
            left: 0,
            right: 0,
            top: 0,
            bottom: 0);

        var shellStyle = new UiStyle(
            Background: effectiveStyle.Background,
            Foreground: effectiveStyle.Foreground,
            Padding: 0,
            BorderColor: effectiveStyle.BorderColor,
            BorderThickness: effectiveStyle.BorderThickness);

        var width = size == ButtonSize.Icon ? effectiveStyle.Height : effectiveStyle.Width;

        return UI.Rect(labelNode, id: id, width: width, height: effectiveStyle.Height, style: shellStyle) with
        {
            Semantics = new UiSemantics(UiRole.Button, text, Disabled: disabled, Focusable: !disabled),
            DeclaredAction = disabled ? null : action,
        };
    }

    private static TextStyle ResolveLabelTextStyle(StandardButtonStyle style, ButtonSize size, bool explicitStyleProvided)
    {
        var labelColor = style.TextStyle.Color ?? style.Foreground;
        if (explicitStyleProvided)
        {
            return style.TextStyle with
            {
                Color = labelColor,
            };
        }

        return style.TextStyle with
        {
            Color = labelColor,
            Size = ResolveTextSize(size),
            AlignX = TextAlignX.Center,
            AlignY = TextAlignY.Center,
        };
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

    private static NodeId? CreateChildId(NodeId? id, string suffix)
    {
        if (id is not { } value)
        {
            return null;
        }

        return new NodeId($"{value.Value}.{suffix}");
    }
}
