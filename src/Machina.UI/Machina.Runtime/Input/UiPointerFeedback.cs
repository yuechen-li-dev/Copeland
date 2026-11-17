using Machina.Core.Semantics;
using Machina.Layout.Rows;

namespace Machina.Runtime.Input;

/// <summary>
/// Window-local visual feedback for hosts using ordinary declared actions.
/// Never invokes actions or owns application state. Refresh after layout so a
/// stationary pointer cannot retain a removed or covered control.
/// </summary>
public sealed class UiPointerFeedback
{
    private UiHitTestIndex? index;
    private PointerPoint? position;
    private UiHitTestResult? hit;
    private UiHitTestResult? pressed;

    public NodeId? Hovered => hit?.Semantics?.Disabled == true ? null : hit?.NodeId;
    public NodeId? Pressed => pressed?.NodeId;
    public bool IsPressed(NodeId id) => Pressed == id && Hovered == id;
    public UiCursor Cursor => UiCursorPolicy.Resolve((pressed ?? hit)?.Semantics,
        (pressed ?? hit) is not null && (pressed ?? hit)!.Semantics?.Role != UiRole.Container);

    public bool Refresh(UiHitTestIndex current)
    {
        index = current;
        if (pressed is not null && !current.Contains(pressed.NodeId)) pressed = null;
        return position is { } point && Move(point);
    }

    public bool Move(PointerPoint point)
    {
        var old = Hovered;
        position = point;
        hit = index?.HitTest(point);
        return old != Hovered;
    }

    public bool Press(PointerPoint point)
    {
        bool changed = Move(point);
        var next = hit?.Semantics?.Disabled == true ? null : hit;
        changed |= pressed?.NodeId != next?.NodeId;
        pressed = next;
        return changed;
    }

    public bool Release(PointerPoint point)
    {
        bool changed = Move(point) || pressed is not null;
        pressed = null;
        return changed;
    }

    public bool Clear()
    {
        bool changed = hit is not null || pressed is not null;
        position = null;
        hit = null;
        pressed = null;
        return changed;
    }
}
