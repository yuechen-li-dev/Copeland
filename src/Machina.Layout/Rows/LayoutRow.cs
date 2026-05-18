using Machina.Layout.Frames;

namespace Machina.Layout.Rows;

public sealed record LayoutRow(
    NodeId Id,
    FrameSpec Frame,
    NodeId? Parent = null,
    int Order = 0,
    int Z = 0,
    string? View = null,
    string? Slot = null,
    string? DebugLabel = null,
    string? Layer = null,
    ArrangeSpec? Arrange = null);
