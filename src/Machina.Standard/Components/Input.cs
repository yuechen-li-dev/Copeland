using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Input
{
    public static UiNode Create(NodeId? id = null, string? value = null, string? placeholder = null, bool disabled = false, UiAction? changed = null, StandardTheme? theme = null, StandardInputStyle? style = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var effectiveStyle = style ?? effectiveTheme.Input.Default;
        var text = string.IsNullOrEmpty(value) ? placeholder ?? string.Empty : value;
        var placeholderMode = string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeholder);
        var textStyle = placeholderMode ? effectiveStyle.PlaceholderTextStyle : effectiveStyle.TextStyle;
        var foreground = disabled
            ? effectiveStyle.DisabledForeground
            : textStyle.Color ?? effectiveStyle.Foreground;

        var textNode = UI.Text(
            text,
            id: CreateChildId(id, "text"),
            size: textStyle.Size,
            alignX: textStyle.AlignX,
            alignY: textStyle.AlignY,
            style: textStyle with { Color = foreground, AlignY = TextAlignY.Center });
        var content = UI.Anchor(textNode, id: CreateChildId(id, "content"), left: effectiveStyle.ContentInset, right: effectiveStyle.ContentInset, top: effectiveStyle.ContentInset, bottom: effectiveStyle.ContentInset);

        var shellForeground = disabled ? effectiveStyle.DisabledForeground : effectiveStyle.Foreground;
        var shellStyle = new UiStyle(disabled ? effectiveStyle.DisabledBackground : effectiveStyle.Background, shellForeground, 0, effectiveStyle.BorderColor, effectiveStyle.BorderThickness);

        return UI.Rect(content, id: id, width: effectiveStyle.Width, height: effectiveStyle.Height, style: shellStyle) with
        {
            Semantics = new UiSemantics(UiRole.Input, text, Disabled: disabled, Focusable: !disabled),
            DeclaredAction = disabled ? null : changed,
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
