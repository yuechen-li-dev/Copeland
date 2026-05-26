using Machina.Core.Styling;
using Machina.Layout.Frames;

namespace Machina.Core.Nodes;

public sealed record LayerNode(
    FrameSpec? Frame,
    UiStyle? Style,
    IReadOnlyList<UiNode> Children,
    double? Width = null,
    double? Height = null) : UiNode;
