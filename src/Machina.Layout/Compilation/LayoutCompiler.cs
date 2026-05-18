using Machina.Layout.Diagnostics;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Rows;

namespace Machina.Layout.Compilation;

public static class LayoutCompiler
{
    public static LayoutDocument CompileLayoutRows(IReadOnlyList<LayoutRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var indexedRows = rows.Select((row, index) => new IndexedRow(row, index)).ToArray();

        ValidateIdAndFrame(indexedRows);

        var duplicate = indexedRows
            .GroupBy(x => x.Row.Id)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new LayoutError("DuplicateNodeId", $"Duplicate node id '{duplicate.Key}'.");
        }

        var roots = indexedRows.Where(x => x.Row.Parent is null).ToArray();
        if (roots.Length == 0)
        {
            throw new LayoutError("MissingRoot", "Layout row graph must contain exactly one root node, but none were parentless.");
        }

        if (roots.Length > 1)
        {
            throw new LayoutError("MultipleRoots", $"Layout row graph must contain exactly one root node, but found {roots.Length} parentless rows.");
        }

        var idSet = indexedRows.Select(x => x.Row.Id).ToHashSet();

        foreach (var indexed in indexedRows)
        {
            if (indexed.Row.Parent is { } parentId && !idSet.Contains(parentId))
            {
                throw new LayoutError("UnknownParent", $"Node '{indexed.Row.Id}' references unknown parent '{parentId}'.");
            }

            if (indexed.Row.Parent is not null && indexed.Row.Frame is RootFrame)
            {
                throw new LayoutError("RootFrameNotRoot", $"Node '{indexed.Row.Id}' uses RootFrame but is not the root node.");
            }
        }

        DetectCycles(indexedRows);

        var nodes = indexedRows.ToDictionary(
            x => x.Row.Id,
            x => new LayoutNode(
                x.Row.Id,
                x.Row.Frame,
                x.Row.Order,
                x.Row.Z,
                x.Row.View,
                x.Row.Slot,
                x.Row.DebugLabel,
                x.Row.Layer,
                x.Row.Arrange));

        var children = new Dictionary<NodeId, IReadOnlyList<NodeId>>(nodes.Count);
        foreach (var id in nodes.Keys)
        {
            children[id] = Array.Empty<NodeId>();
        }

        var childrenByParent = indexedRows
            .Where(x => x.Row.Parent is not null)
            .GroupBy(x => x.Row.Parent!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<NodeId>)group
                    .OrderBy(x => x.Row.Order)
                    .ThenBy(x => x.Index)
                    .Select(x => x.Row.Id)
                    .ToArray());

        foreach (var pair in childrenByParent)
        {
            children[pair.Key] = pair.Value;
        }

        return new LayoutDocument(roots[0].Row.Id, nodes, children);
    }

    private static void ValidateIdAndFrame(IEnumerable<IndexedRow> rows)
    {
        foreach (var indexed in rows)
        {
            var id = indexed.Row.Id.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new LayoutError("InvalidNodeId", $"Row at source index {indexed.Index} has an invalid node id.");
            }

            if (indexed.Row.Frame is null)
            {
                throw new LayoutError("MissingFrame", $"Node '{indexed.Row.Id}' is missing frame data.");
            }
        }
    }

    private static void DetectCycles(IReadOnlyList<IndexedRow> rows)
    {
        var byId = rows.ToDictionary(x => x.Row.Id, x => x.Row);
        var state = new Dictionary<NodeId, VisitState>(rows.Count);

        foreach (var nodeId in byId.Keys)
        {
            if (Visit(nodeId, byId, state))
            {
                throw new LayoutError("CycleDetected", $"Cycle detected in layout row graph involving node '{nodeId}'.");
            }
        }
    }

    private static bool Visit(
        NodeId nodeId,
        IReadOnlyDictionary<NodeId, LayoutRow> byId,
        IDictionary<NodeId, VisitState> state)
    {
        if (state.TryGetValue(nodeId, out var nodeState))
        {
            return nodeState == VisitState.Visiting;
        }

        state[nodeId] = VisitState.Visiting;

        var row = byId[nodeId];
        if (row.Parent is { } parentId && Visit(parentId.Value, byId, state))
        {
            return true;
        }

        state[nodeId] = VisitState.Visited;
        return false;
    }

    private readonly record struct IndexedRow(LayoutRow Row, int Index);

    private enum VisitState
    {
        Visiting,
        Visited,
    }
}
