using Machina.Core.Styling;

namespace Machina.Core.Nodes;

public sealed record RectNode(
    UiNode? Child = null,
    double? Width = null,
    double? Height = null,
    ColorToken? Color = null,
    double Padding = 0,
    UiStyle? Style = null) : UiNode;
