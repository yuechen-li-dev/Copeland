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
        var children = new List<UiNode>
        {
            StandardUI.Place(content, 0, -offset, width, Math.Max(1, contentHeight))
        };
        if (contentHeight > height)
        {
            double thumbHeight = Math.Max(12, height * height / contentHeight);
            thumbHeight = Math.Min(height, thumbHeight);
            double top = offset / (contentHeight - height) * (height - thumbHeight);
            children.Add(StandardUI.Place(Box(id.Child("scroll-indicator"), 3, thumbHeight, Style.Muted),
                Math.Max(0, width - 3), top));
        }
        return Bind(new(id, id, WidgetKind.Scroll),
            Panel(id, width, height, children, new UiStyle(ClipToBounds: true)),
            new(UiRole.Group, label, Focusable: contentHeight > height));
    }
}
