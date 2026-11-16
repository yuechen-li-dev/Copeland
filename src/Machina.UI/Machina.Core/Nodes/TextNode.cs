using Machina.Core.Styling;

namespace Machina.Core.Nodes;

public sealed record TextNode(
    string Text,
    TextStyle? Style = null) : UiNode
{
    /// <summary>Use the parent's available layout box for text alignment rather than the intrinsic run size.</summary>
    public bool FillAvailableSpace { get; init; }
}
