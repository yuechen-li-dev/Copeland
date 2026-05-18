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
        var style = new UiStyle(
            Background: effectiveTheme.Colors.Background,
            Foreground: effectiveTheme.Colors.Foreground,
            Padding: effectiveTheme.Spacing.Lg);

        return UI.Rect(
            child,
            id,
            width,
            height,
            color: null,
            padding: null,
            style);
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
}
