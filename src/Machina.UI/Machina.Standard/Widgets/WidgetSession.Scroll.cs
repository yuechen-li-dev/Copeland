using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Runtime.Input;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetSession
{
    private readonly Dictionary<NodeId, double> scrollOffsets = [];
    private List<WidgetScrollRegion> scrollRegions = [];
    private readonly Dictionary<NodeId, HashSet<NodeId>> scrollChildren = [];
    private NodeId? scrollFocus;
    internal bool NeedsReprepare { get; private set; }

    internal double ScrollOffset(NodeId id, double contentHeight, double viewportHeight) =>
        Math.Clamp(scrollOffsets.GetValueOrDefault(id), 0, Math.Max(0, contentHeight - viewportHeight));

    private void PresentScroll(WidgetBuilder builder, ResolvedLayoutDocument resolved)
    {
        NeedsReprepare = false;
        scrollRegions = builder.ScrollRegions.Where(region => resolved.Nodes.ContainsKey(region.Id)).ToList();
        var active = scrollRegions.Select(region => region.Id).ToHashSet();
        foreach (var id in scrollOffsets.Keys.Where(id => !active.Contains(id)).ToArray()) scrollOffsets.Remove(id);
        scrollChildren.Clear();
        foreach (var region in scrollRegions)
        {
            scrollChildren[region.Id] = Descendants(resolved, region.Id);
            scrollOffsets[region.Id] = ScrollOffset(region.Id, region.ContentHeight, region.ViewportHeight);
        }
        if (State.Focus != scrollFocus && State.Focus is { } focused) EnsureVisible(focused);
        scrollFocus = State.Focus;
    }

    private void EnsureVisible(NodeId id)
    {
        scrollFocus = id;
        if (!rectangles.TryGetValue(id, out var item)) return;
        foreach (var region in scrollRegions.AsEnumerable().Reverse())
        {
            if (id == region.Id || !scrollChildren[region.Id].Contains(id)) continue;
            Rect viewport = rectangles[region.Id];
            double delta = item.Y < viewport.Y ? item.Y - viewport.Y
                : item.Y + item.Height > viewport.Y + viewport.Height
                    ? item.Y + item.Height - viewport.Y - viewport.Height : 0;
            SetScroll(region, scrollOffsets[region.Id] + delta);
        }
    }

    private void SetScroll(WidgetScrollRegion region, double value)
    {
        double next = Math.Clamp(value, 0, Math.Max(0, region.ContentHeight - region.ViewportHeight));
        if (next == scrollOffsets.GetValueOrDefault(region.Id)) return;
        scrollOffsets[region.Id] = next;
        NeedsReprepare = true;
    }

    private bool Wheel(UiPointerWheel input)
    {
        NodeId? hit = hitTest?.HitTest(input.Position)?.NodeId;
        if (hit is null) return false;
        foreach (var region in scrollRegions.AsEnumerable().Reverse())
        {
            if (!scrollChildren[region.Id].Contains(hit.Value)) continue;
            SetScroll(region, scrollOffsets[region.Id] - input.DeltaY * 36);
            return true;
        }
        return false;
    }

    private bool ScrollKey(WidgetBinding binding, UiKey key)
    {
        var region = scrollRegions.FirstOrDefault(item => item.Id == binding.Id);
        if (region is null) return false;
        double current = scrollOffsets[region.Id];
        double next;
        switch (key)
        {
            case UiKey.ArrowDown: next = current + 36; break;
            case UiKey.ArrowUp: next = current - 36; break;
            case UiKey.PageDown: next = current + region.ViewportHeight; break;
            case UiKey.PageUp: next = current - region.ViewportHeight; break;
            case UiKey.Home: next = 0; break;
            case UiKey.End: next = region.ContentHeight; break;
            default: return false;
        }
        SetScroll(region, next);
        return true;
    }
}
