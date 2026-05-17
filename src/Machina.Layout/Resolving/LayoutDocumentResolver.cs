using Machina.Layout.Diagnostics;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Layout.Resolving;

public static class LayoutDocumentResolver
{
    public static ResolvedLayoutDocument ResolveLayoutDocument(
        LayoutDocument document,
        Rect rootRect)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateRootRect(rootRect);

        if (!document.Nodes.ContainsKey(document.RootId))
        {
            throw new LayoutError("MissingRootNode", $"Root node '{document.RootId}' does not exist in document nodes.");
        }

        ValidateChildrenEntries(document);

        var resolvedNodes = new Dictionary<NodeId, ResolvedLayoutNode>(document.Nodes.Count);
        var visitState = new Dictionary<NodeId, VisitState>(document.Nodes.Count);

        ResolveNode(document.RootId, rootRect, document, resolvedNodes, visitState);

        if (resolvedNodes.Count != document.Nodes.Count)
        {
            var unreachable = document.Nodes.Keys.First(id => !resolvedNodes.ContainsKey(id));
            throw new LayoutError("UnreachableNode", $"Node '{unreachable}' is not reachable from root '{document.RootId}'.");
        }

        return new ResolvedLayoutDocument(
            document.RootId,
            resolvedNodes,
            new Dictionary<NodeId, IReadOnlyList<NodeId>>(document.Children));
    }

    private static void ResolveNode(
        NodeId nodeId,
        Rect resolvedRect,
        LayoutDocument document,
        IDictionary<NodeId, ResolvedLayoutNode> resolvedNodes,
        IDictionary<NodeId, VisitState> visitState)
    {
        if (visitState.TryGetValue(nodeId, out var existingState))
        {
            if (existingState == VisitState.Visiting)
            {
                throw new LayoutError("DocumentCycleDetected", $"Cycle detected while resolving node '{nodeId}'.");
            }

            if (existingState == VisitState.Visited)
            {
                return;
            }
        }

        visitState[nodeId] = VisitState.Visiting;

        var node = document.Nodes[nodeId];
        resolvedNodes[nodeId] = new ResolvedLayoutNode(
            node.Id,
            resolvedRect,
            node.Frame,
            node.Order,
            node.Z,
            node.View,
            node.Slot,
            node.DebugLabel,
            node.Layer);

        var childIds = document.Children[nodeId];
        foreach (var childId in childIds)
        {
            if (!document.Nodes.TryGetValue(childId, out var childNode))
            {
                throw new LayoutError("UnknownChildNode", $"Children entry for '{nodeId}' references unknown child '{childId}'.");
            }

            if (!document.Children.ContainsKey(childId))
            {
                throw new LayoutError("MissingChildrenEntry", $"Node '{childId}' is missing a children entry.");
            }

            var childRect = FrameResolver.ResolveFrame(resolvedRect, childNode.Frame);
            ResolveNode(childId, childRect, document, resolvedNodes, visitState);
        }

        visitState[nodeId] = VisitState.Visited;
    }

    private static void ValidateRootRect(Rect rootRect)
    {
        ValidateFinite(rootRect.X, nameof(rootRect.X));
        ValidateFinite(rootRect.Y, nameof(rootRect.Y));
        ValidateFinite(rootRect.Width, nameof(rootRect.Width));
        ValidateFinite(rootRect.Height, nameof(rootRect.Height));

        if (rootRect.Width < 0 || rootRect.Height < 0)
        {
            throw new LayoutError("InvalidRootRect", "Root rect width and height must be non-negative.");
        }
    }

    private static void ValidateChildrenEntries(LayoutDocument document)
    {
        if (!document.Children.ContainsKey(document.RootId))
        {
            throw new LayoutError("MissingChildrenEntry", $"Root node '{document.RootId}' is missing a children entry.");
        }

        foreach (var nodeId in document.Nodes.Keys)
        {
            if (!document.Children.ContainsKey(nodeId))
            {
                throw new LayoutError("MissingChildrenEntry", $"Node '{nodeId}' is missing a children entry.");
            }
        }

        foreach (var (parentId, children) in document.Children)
        {
            if (!document.Nodes.ContainsKey(parentId))
            {
                throw new LayoutError("UnknownChildNode", $"Children map contains unknown parent '{parentId}'.");
            }

            foreach (var childId in children)
            {
                if (!document.Nodes.ContainsKey(childId))
                {
                    throw new LayoutError("UnknownChildNode", $"Children entry for '{parentId}' references unknown child '{childId}'.");
                }
            }
        }
    }

    private static void ValidateFinite(double value, string field)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new LayoutError("InvalidRootRect", $"Root rect field '{field}' must be finite.");
        }
    }

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}
