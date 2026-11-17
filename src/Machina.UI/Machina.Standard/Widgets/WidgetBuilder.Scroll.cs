using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetBuilder
{
    internal List<WidgetScrollRegion> ScrollRegions { get; } = [];
    /// <summary>Width reserved beside overflowing content for the scroll indicator.</summary>
    public const double ScrollbarGutter = 8;

    /// <summary>
    /// A clipped vertical viewport with wheel, keyboard and focus-into-view behavior.
    /// Offset is transient window state; content is not virtualized. Dense lists
    /// should provide a virtualized declaration rather than one node per record.
    /// </summary>
    public UiNode ScrollArea(NodeId id, UiNode content, double contentHeight,
        double width = 240, double height = 180, string label = "Scrollable content")
    {
        ValidateWidth(width);
        ValidateWidth(height);
        if (!double.IsFinite(contentHeight) || contentHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(contentHeight));
        ScrollRegions.Add(new(id, contentHeight, height));
        double offset = Session.ScrollOffset(id, contentHeight, height);
        bool overflow = contentHeight > height;
        // Overflowing content gets a gutter for the indicator; its own clip means even content wider than the
        // viewport can never draw underneath the indicator.
        double viewportWidth = Math.Max(1, width - (overflow ? ScrollbarGutter : 0));
        var children = new List<UiNode>
        {
            StandardUI.Place(UI.Rect(UI.Layer(children: [StandardUI.Place(content, 0, -offset, viewportWidth, Math.Max(1, contentHeight))]),
                id.Child("viewport"), viewportWidth, height, style: new UiStyle(ClipToBounds: true)), 0, 0, viewportWidth, height)
        };
        if (overflow)
        {
            // Track inset 4 px top and bottom: clear of the 2 px focus ring.
            double track = Math.Max(1, height - 8);
            double thumbHeight = Math.Min(track, Math.Max(12, track * height / contentHeight));
            double top = 4 + offset / (contentHeight - height) * (track - thumbHeight);
            children.Add(StandardUI.Place(Box(id.Child("scroll-indicator"), 3, thumbHeight, Style.Muted),
                width - ScrollbarGutter + (ScrollbarGutter - 3) / 2, top));
        }
        // The focus ring is the last child, so it draws above the content instead of being painted over by it.
        if (Session.Focused == id && Session.FocusVisible)
            children.Add(StandardUI.Place(UI.Rect(id: id.Child("focus-ring"), width: width, height: height,
                style: new UiStyle(BorderColor: Style.Focus, BorderThickness: 2)), 0, 0, width, height));
        return Bind(new(id, id, WidgetKind.Scroll),
            Panel(id, width, height, children, new UiStyle(ClipToBounds: true)),
            new(UiRole.Group, label, Focusable: contentHeight > height));
    }
}
