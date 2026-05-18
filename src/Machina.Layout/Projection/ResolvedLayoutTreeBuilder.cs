using Machina.Layout.Diagnostics;
using Machina.Layout.Documents;
using Machina.Layout.Rows;

namespace Machina.Layout.Projection;

public static class ResolvedLayoutTreeBuilder
{
    public static ResolvedLayoutTree ToResolvedTree(ResolvedLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateResolvedDocument(document);

        return BuildNode(document.RootId, document, new Dictionary<NodeId, VisitState>(document.Nodes.Count));
    }

    private static ResolvedLayoutTree BuildNode(
        NodeId nodeId,
        ResolvedLayoutDocument document,
        IDictionary<NodeId, VisitState> visitState)
    {
        if (visitState.TryGetValue(nodeId, out var state))
        {
            if (state == VisitState.Visiting)
            {
                throw new LayoutError("ResolvedDocumentCycleDetected", $"Cycle detected while projecting resolved node '{nodeId}'.");
            }

            throw new LayoutError("ResolvedDocumentCycleDetected", $"Node '{nodeId}' was visited more than once while projecting resolved tree.");
        }

        visitState[nodeId] = VisitState.Visiting;

        var node = document.Nodes[nodeId];
        var childTrees = new List<ResolvedLayoutTree>(document.Children[nodeId].Count);
        foreach (var childId in document.Children[nodeId])
        {
            childTrees.Add(BuildNode(childId, document, visitState));
        }

        visitState[nodeId] = VisitState.Visited;

        return new ResolvedLayoutTree(
            node.Id,
            node.Rect,
            node.Frame,
            node.Order,
            node.Z,
            node.View,
            node.Slot,
            node.DebugLabel,
            node.Layer,
            childTrees,
            node.Arrange);
    }

    private static void ValidateResolvedDocument(ResolvedLayoutDocument document)
    {
        if (!document.Nodes.ContainsKey(document.RootId))
        {
            throw new LayoutError("MissingResolvedRootNode", $"Root node '{document.RootId}' does not exist in resolved nodes.");
        }

        if (!document.Children.ContainsKey(document.RootId))
        {
            throw new LayoutError("MissingResolvedChildrenEntry", $"Root node '{document.RootId}' is missing a resolved children entry.");
        }

        foreach (var nodeId in document.Nodes.Keys)
        {
            if (!document.Children.ContainsKey(nodeId))
            {
                throw new LayoutError("MissingResolvedChildrenEntry", $"Node '{nodeId}' is missing a resolved children entry.");
            }
        }

        foreach (var (parentId, children) in document.Children)
        {
            if (!document.Nodes.ContainsKey(parentId))
            {
                throw new LayoutError("UnknownResolvedChildNode", $"Resolved children map contains unknown parent '{parentId}'.");
            }

            foreach (var childId in children)
            {
                if (!document.Nodes.ContainsKey(childId))
                {
                    throw new LayoutError("UnknownResolvedChildNode", $"Resolved children entry for '{parentId}' references unknown child '{childId}'.");
                }
            }
        }

        var visited = new HashSet<NodeId>();
        var visiting = new HashSet<NodeId>();
        Traverse(document.RootId, document, visited, visiting);

        if (visited.Count != document.Nodes.Count)
        {
            var unreachable = document.Nodes.Keys.First(id => !visited.Contains(id));
            throw new LayoutError("UnreachableResolvedNode", $"Node '{unreachable}' is not reachable from root '{document.RootId}'.");
        }
    }

    private static void Traverse(
        NodeId nodeId,
        ResolvedLayoutDocument document,
        ISet<NodeId> visited,
        ISet<NodeId> visiting)
    {
        if (visited.Contains(nodeId))
        {
            return;
        }

        if (!visiting.Add(nodeId))
        {
            throw new LayoutError("ResolvedDocumentCycleDetected", $"Cycle detected while validating resolved node '{nodeId}'.");
        }

        foreach (var childId in document.Children[nodeId])
        {
            Traverse(childId, document, visited, visiting);
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}
