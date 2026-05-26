using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Card
{
    public static UiNode Create(
        UiNode child,
        NodeId? id = null,
        StandardTheme? theme = null,
        double? width = null,
        double? height = null)
    {
        ArgumentNullException.ThrowIfNull(child);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var inset = effectiveTheme.Spacing.Sm;
        var content = UI.Anchor(
            child,
            id: CreateChildId(id, "content"),
            left: inset,
            right: inset,
            top: inset,
            bottom: inset);

        var style = new UiStyle(
            Background: effectiveTheme.Colors.Background,
            Foreground: effectiveTheme.Colors.Foreground,
            Padding: 0,
            BorderColor: effectiveTheme.Colors.Border,
            BorderThickness: 1);

        return UI.Rect(
            content,
            id,
            width,
            height,
            color: null,
            padding: null,
            style: style);
    }

    public static UiNode Create(
        IReadOnlyList<UiNode> children,
        NodeId? id = null,
        StandardTheme? theme = null,
        double? width = null,
        double? height = null,
        double? gap = null)
    {
        ArgumentNullException.ThrowIfNull(children);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var child = UI.Column(
            children,
            gap: gap ?? effectiveTheme.Spacing.Sm);

        return Create(
            child,
            id,
            effectiveTheme,
            width,
            height);
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
