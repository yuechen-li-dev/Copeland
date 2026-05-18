using Machina.Layout.Frames;

namespace Machina.Core.Nodes;

public sealed record StackNode(
    StackAxis Axis,
    IReadOnlyList<UiNode> Children,
    double Gap = 0,
    double Padding = 0) : UiNode;
