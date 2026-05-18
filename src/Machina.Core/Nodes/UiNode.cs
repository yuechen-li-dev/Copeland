using Machina.Layout.Rows;

namespace Machina.Core.Nodes;

public abstract record UiNode
{
    public NodeId? Id { get; init; }
}
