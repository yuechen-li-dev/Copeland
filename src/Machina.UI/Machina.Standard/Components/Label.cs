using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Label
{
    public static UiNode Create(
        string text,
        NodeId? id = null,
        StandardTheme? theme = null,
        TextSize size = TextSize.Md,
        ColorToken? color = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var style = new TextStyle(
            Color: color ?? effectiveTheme.Colors.Foreground,
            Size: size);

        return UI.Text(
            text,
            id,
            size: size,
            style: style) with
        {
            Semantics = new UiSemantics(UiRole.Label, text),
        };
    }
}
