using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Text;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class TextBlock
{
    public static UiNode Create(
        MachinaTextSpec text,
        NodeId? id = null,
        StandardTheme? theme = null,
        ColorToken? foreground = null,
        ColorToken? linkForeground = null,
        double? width = null,
        double? height = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var metadata = new StandardTextBlockMetadata(
            text,
            foreground ?? effectiveTheme.Colors.Foreground,
            linkForeground ?? effectiveTheme.Colors.Primary);

        return UI.Rect(
            child: new RichTextNode(metadata)
            {
                Id = CreateChildId(id, "content"),
            },
            id: id,
            width: width,
            height: height);
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
