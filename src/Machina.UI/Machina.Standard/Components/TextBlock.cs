using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Text;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class TextBlock
{
    public static RectNode Create(
        MachinaTextSpec text,
        NodeId? id = null,
        StandardTheme? theme = null,
        ColorToken? foreground = null,
        ColorToken? linkForeground = null,
        double? width = null,
        double? height = null,
        IMachinaTextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var metadata = new StandardTextBlockMetadata(
            text,
            foreground ?? effectiveTheme.Colors.Foreground,
            linkForeground ?? effectiveTheme.Colors.Primary,
            measurer);

        return new RectNode(
            Child: new RichTextNode(metadata)
            {
                Id = CreateChildId(id, "content"),
            },
            Width: width,
            Height: height,
            Style: new UiStyle())
        {
            Id = id,
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
