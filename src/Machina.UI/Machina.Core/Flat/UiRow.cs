using Machina.Layout.Frames;
using Machina.Layout.Rows;

namespace Machina.Core.Flat;

public sealed record UiRow(
    NodeId Id,
    NodeId? Parent,
    FrameSpec Frame,
    ArrangeSpec? Arrange = null,
    int Order = 0,
    UiView? View = null,
    Machina.Core.Nodes.UiNode? Component = null);
