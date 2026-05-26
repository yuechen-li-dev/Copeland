using Machina.Layout.Frames;

namespace Machina.Core.Nodes;

public sealed record PlacementNode(
    FrameSpec Frame,
    UiNode Child) : UiNode;
