using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Layout.Documents;

public sealed record ResolvedLayoutNode(
    NodeId Id,
    Rect Rect,
    FrameSpec Frame,
    int Order,
    int Z,
    string? View,
    string? Slot,
    string? DebugLabel,
    string? Layer,
    ArrangeSpec? Arrange = null);
