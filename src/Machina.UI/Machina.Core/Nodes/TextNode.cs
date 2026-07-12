using Machina.Core.Styling;

namespace Machina.Core.Nodes;

public sealed record TextNode(
    string Text,
    TextStyle? Style = null) : UiNode;
