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
        StandardTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var style = new TextStyle(
            Color: effectiveTheme.Colors.Foreground,
            Size: TextSize.Sm);

        return UI.Text(
            text,
            id,
            style: style) with
        {
            Semantics = new UiSemantics(UiRole.Label, text),
        };
    }
}
