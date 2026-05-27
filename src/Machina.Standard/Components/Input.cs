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
        var foreground = disabled ? effectiveStyle.DisabledForeground : (placeholderMode ? effectiveStyle.PlaceholderForeground : effectiveStyle.TextStyle.Color ?? effectiveStyle.Foreground);

        var textNode = UI.Text(text, id: CreateChildId(id, "text"), style: effectiveStyle.TextStyle with { Color = foreground, AlignY = TextAlignY.Center });
        var content = UI.Anchor(textNode, id: CreateChildId(id, "content"), left: effectiveStyle.ContentInset, right: effectiveStyle.ContentInset, top: effectiveStyle.ContentInset, bottom: effectiveStyle.ContentInset);

        var shellStyle = new UiStyle(disabled ? effectiveStyle.DisabledBackground : effectiveStyle.Background, foreground, 0, effectiveStyle.BorderColor, effectiveStyle.BorderThickness);

        return UI.Rect(content, id: id, height: effectiveStyle.Height, style: shellStyle) with
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
