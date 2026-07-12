using Machina.Layout.Rows;

namespace Machina.Layout.Documents;

public sealed record ResolvedLayoutDocument(
    NodeId RootId,
    IReadOnlyDictionary<NodeId, ResolvedLayoutNode> Nodes,
    IReadOnlyDictionary<NodeId, IReadOnlyList<NodeId>> Children);
