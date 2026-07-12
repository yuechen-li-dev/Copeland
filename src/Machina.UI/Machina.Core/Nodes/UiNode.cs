using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Layout.Rows;

namespace Machina.Core.Nodes;

public abstract record UiNode
{
    public NodeId? Id { get; init; }

    public UiSemantics? Semantics { get; init; }

    public UiAction? DeclaredAction { get; init; }
}
