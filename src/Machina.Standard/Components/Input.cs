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
    public static UiNode Create(
        NodeId? id = null,
        string? value = null,
        string? placeholder = null,
        bool disabled = false,
        UiAction? changed = null,
        StandardTheme? theme = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var text = ResolveDisplayText(value, placeholder);
        var isPlaceholder = string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeholder);
        var textColor = ResolveTextColor(effectiveTheme, disabled, isPlaceholder);
        var background = disabled
            ? effectiveTheme.Colors.Muted
            : effectiveTheme.Colors.Background;

        var textNode = UI.Text(
            text,
            id: CreateChildId(id, "text"),
            style: new TextStyle(
                Color: textColor,
                Size: TextSize.Md));

        var style = new UiStyle(
            Background: background,
            Foreground: textColor,
            Padding: effectiveTheme.Spacing.Sm,
            BorderColor: effectiveTheme.Colors.Border,
            BorderThickness: 1);

        return UI.Rect(
            child: textNode,
            id: id,
            height: 36,
            style: style) with
        {
            Semantics = new UiSemantics(
                UiRole.Input,
                text,
                Disabled: disabled,
                Focusable: !disabled),
            DeclaredAction = disabled ? null : changed,
        };
    }

    private static string ResolveDisplayText(
        string? value,
        string? placeholder)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        return placeholder ?? string.Empty;
    }

    private static ColorToken ResolveTextColor(
        StandardTheme theme,
        bool disabled,
        bool isPlaceholder)
    {
        if (disabled || isPlaceholder)
        {
            return theme.Colors.MutedForeground;
        }

        return theme.Colors.Foreground;
    }

    private static NodeId? CreateChildId(
        NodeId? id,
        string suffix)
    {
        if (id is not { } value)
        {
            return null;
        }

        return new NodeId($"{value.Value}.{suffix}");
    }
}
