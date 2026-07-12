using Machina.Layout.Frames;

namespace Machina.Core.Nodes;

public sealed record StackNode(
    StackAxis Axis,
    IReadOnlyList<UiStackItem> Items,
    double Gap = 0,
    EdgeInsets Padding = default) : UiNode;
