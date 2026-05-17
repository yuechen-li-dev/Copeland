using Machina.Layout.Rows;

namespace Machina.Layout.Documents;

public sealed record LayoutDocument(
    NodeId RootId,
    IReadOnlyDictionary<NodeId, LayoutNode> Nodes,
    IReadOnlyDictionary<NodeId, IReadOnlyList<NodeId>> Children);
