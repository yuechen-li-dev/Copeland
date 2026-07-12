using Machina.Layout.Frames;

namespace Machina.Core.Nodes;

public sealed record SpacerNode(
    StackAxis Axis,
    double Size) : UiNode;
