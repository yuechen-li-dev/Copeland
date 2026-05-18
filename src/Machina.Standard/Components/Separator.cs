using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Separator
{
    public static UiNode Create(
        NodeId? id = null,
        StackAxis axis = StackAxis.Horizontal,
        double thickness = 1,
        StandardTheme? theme = null)
    {
        if (!double.IsFinite(thickness) || thickness <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thickness),
                thickness,
                "Separator thickness must be a positive finite number.");
        }

        var effectiveTheme = theme ?? StandardTheme.Default;
        var width = axis == StackAxis.Horizontal ? 100 : thickness;
        var height = axis == StackAxis.Horizontal ? thickness : 100;

        return UI.Rect(
            id: id,
            width: width,
            height: height,
            color: effectiveTheme.Colors.Border);
    }
}
