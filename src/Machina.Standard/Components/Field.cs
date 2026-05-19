using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Field
{
    public static UiNode Create(
        UiNode control,
        NodeId? id = null,
        string? label = null,
        string? description = null,
        string? error = null,
        StandardTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(control);

        var effectiveTheme = theme ?? StandardTheme.Default;
        var children = new List<UiNode>();

        if (!string.IsNullOrEmpty(label))
        {
            children.Add(Label.Create(
                label,
                CreateChildId(id, "label"),
                effectiveTheme));
        }

        children.Add(control);

        if (!string.IsNullOrEmpty(description))
        {
            children.Add(UI.Text(
                description,
                id: CreateChildId(id, "description"),
                style: new TextStyle(
                    Color: effectiveTheme.Colors.MutedForeground,
                    Size: TextSize.Sm)));
        }

        if (!string.IsNullOrEmpty(error))
        {
            children.Add(UI.Text(
                error,
                id: CreateChildId(id, "error"),
                style: new TextStyle(
                    Color: effectiveTheme.Colors.Destructive,
                    Size: TextSize.Sm)));
        }

        return UI.Column(
            children,
            id,
            gap: effectiveTheme.Spacing.Xs);
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
