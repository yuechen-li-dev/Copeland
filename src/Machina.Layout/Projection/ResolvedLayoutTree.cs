using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Layout.Projection;

public sealed record ResolvedLayoutTree(
    NodeId Id,
    Rect Rect,
    FrameSpec Frame,
    int Order,
    int Z,
    string? View,
    string? Slot,
    string? DebugLabel,
    string? Layer,
    IReadOnlyList<ResolvedLayoutTree> Children,
    ArrangeSpec? Arrange = null);
