using Machina.Layout.Frames;
using Machina.Layout.Rows;

namespace Machina.Layout.Documents;

public sealed record LayoutNode(
    NodeId Id,
    FrameSpec Frame,
    int Order,
    int Z,
    string? View,
    string? Slot,
    string? DebugLabel,
    string? Layer,
    ArrangeSpec? Arrange = null);
