using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Presentation;
using Machina.Runtime.Input;
using Machina.Standard.Theme;

namespace Machina.Pipeline;

/// <summary>Repaints feedback over the authoritative prepared geometry without relayout or model mutation.</summary>
public static class MachinaPointerPresentation
{
    public static MachinaPresentationFrame Apply(MachinaPreparedPresentation prepared, UiPointerFeedback feedback,
        ColorToken surface, ColorToken foreground)
    {
        var styles = prepared.Lowering.Styles.ToDictionary();
        foreach (var node in new[] { feedback.Hovered, feedback.Pressed }.OfType<Machina.Layout.Rows.NodeId>().Distinct())
        {
            var semantics = prepared.Lowering.Semantics.GetValueOrDefault(node);
            if (semantics?.Disabled == true || semantics?.Role is UiRole.Container or UiRole.Group) continue;
            var original = styles.GetValueOrDefault(node) ?? new UiStyle();
            var background = original.Background ?? surface;
            // A transparent row still needs visible feedback against its parent surface.
            if ((background.Rgba & 255) == 0) background = surface;
            styles[node] = original with
            {
                Background = StandardInteraction.Surface(background, original.Foreground ?? foreground,
                    feedback.Hovered == node, feedback.IsPressed(node))
            };
        }
        return MachinaPresentationFrameBuilder.Build(prepared.Lowering with { Styles = styles },
            prepared.Resolved, prepared.PresentationFrame.Viewport);
    }
}
