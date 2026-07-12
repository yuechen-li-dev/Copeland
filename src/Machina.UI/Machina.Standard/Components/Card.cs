using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Card
{
    public static UiNode Create(UiNode child, NodeId? id = null, StandardTheme? theme = null, double? width = null, double? height = null, StandardCardStyle? style = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var effectiveStyle = style ?? effectiveTheme.Card.Default;

        var content = UI.Anchor(child, id: CreateChildId(id, "content"), left: effectiveStyle.ContentInset, right: effectiveStyle.ContentInset, top: effectiveStyle.ContentInset, bottom: effectiveStyle.ContentInset);
        var shellStyle = new UiStyle(effectiveStyle.Background, effectiveStyle.Foreground, 0, effectiveStyle.BorderColor, effectiveStyle.BorderThickness);

        return UI.Rect(content, id, width, height, color: null, padding: null, style: shellStyle);
    }

    public static UiNode Create(IReadOnlyList<UiNode> children, NodeId? id = null, StandardTheme? theme = null, double? width = null, double? height = null, double? gap = null, StandardCardStyle? style = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var child = UI.Column(children, gap: gap ?? effectiveTheme.Spacing.Sm);
        return Create(child, id, effectiveTheme, width, height, style);
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
