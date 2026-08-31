using System.Globalization;
using Copeland.TS.Templates;

namespace Oblivion.App;

internal sealed record AutomaticLayoutPlan(
    IReadOnlyList<OblivionResolvedDiagramNode> Nodes,
    IReadOnlyDictionary<string, string> RouteKinds,
    OblivionNativeLayoutMetrics Metrics,
    IReadOnlyList<OblivionNativeLayoutDiagnostic> Diagnostics);

internal static class AutomaticLayeredLayout
{
    private const double LayerGap = 48;
    private const double NodeGap = 46;
    private const double ComponentGap = 36;
    private const double OuterMargin = 46;
    private const int OrderingSweepCount = 4;

    public static AutomaticLayoutPlan Resolve(
        Diagram diagram,
        Func<string, (double Width, double Height)> measureNode,
        Func<DiagramNode, double, double, double, double, OblivionResolvedDiagramNode> createNode)
    {
        Dictionary<string, DiagramNode> nodesById = diagram.Nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);
        Dictionary<string, string[]> outgoing = diagram.Nodes.ToDictionary(
            node => node.Id,
            node => diagram.Edges
                .Where(edge => edge.From == node.Id && edge.To != node.Id)
                .Select(edge => edge.To)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);

        IReadOnlyList<string[]> stronglyConnected = FindStronglyConnectedComponents(
            diagram.Nodes.Select(node => node.Id),
            outgoing);
        Dictionary<string, int> componentByNode = new(StringComparer.Ordinal);
        for (int componentIndex = 0; componentIndex < stronglyConnected.Count; componentIndex++)
        {
            foreach (string nodeId in stronglyConnected[componentIndex])
            {
                componentByNode[nodeId] = componentIndex;
            }
        }

        Dictionary<int, int> componentRanks = RankComponents(
            stronglyConnected,
            componentByNode,
            diagram.Edges);
        Dictionary<string, int> ranks = new(StringComparer.Ordinal);
        List<OblivionNativeLayoutDiagnostic> diagnostics = [];
        for (int componentIndex = 0; componentIndex < stronglyConnected.Count; componentIndex++)
        {
            string[] component = stronglyConnected[componentIndex];
            bool cyclic = component.Length > 1 || diagram.Edges.Any(edge =>
                edge.From == component[0] && edge.To == component[0]);
            if (cyclic)
            {
                diagnostics.Add(new OblivionNativeLayoutDiagnostic(
                    "OBLIVION-NATIVE-CYCLE-NORMALIZED",
                    $"A strongly connected region containing {component.Length} node(s) was given a stable forward orientation for ranking."));
            }

            for (int index = 0; index < component.Length; index++)
            {
                ranks[component[index]] = componentRanks[componentIndex] + index;
            }
        }

        Dictionary<string, string> weakComponentKeys = FindWeakComponentKeys(diagram);
        Dictionary<int, List<string>> orderedLayers = CreateInitialLayerOrder(
            diagram,
            ranks,
            weakComponentKeys);
        ImproveLayerOrder(diagram, ranks, orderedLayers);

        Dictionary<string, int> positions = CreatePositions(orderedLayers);
        int crossingEstimate = EstimateAdjacentLayerCrossings(diagram.Edges, ranks, positions);
        IReadOnlyList<OblivionResolvedDiagramNode> resolvedNodes = PlaceNodes(
            diagram,
            ranks,
            orderedLayers,
            weakComponentKeys,
            nodesById,
            measureNode,
            createNode);

        Dictionary<string, string> routeKinds = new(StringComparer.Ordinal);
        int backEdgeCount = 0;
        int crossLayerEdgeCount = 0;
        for (int edgeIndex = 0; edgeIndex < diagram.Edges.Count; edgeIndex++)
        {
            DiagramEdge edge = diagram.Edges[edgeIndex];
            string key = EdgeKey(edge, edgeIndex);
            if (edge.From == edge.To)
            {
                routeKinds[key] = "self-loop";
                backEdgeCount++;
                continue;
            }

            int delta = ranks[edge.To] - ranks[edge.From];
            if (delta <= 0)
            {
                routeKinds[key] = "back-edge";
                backEdgeCount++;
            }
            else if (delta > 1)
            {
                routeKinds[key] = "cross-layer";
                crossLayerEdgeCount++;
            }
            else
            {
                routeKinds[key] = "forward";
            }
        }

        if (backEdgeCount > 0)
        {
            diagnostics.Add(new OblivionNativeLayoutDiagnostic(
                "OBLIVION-NATIVE-BACK-EDGES-ROUTED",
                $"{backEdgeCount} back edge(s) or self edge(s) were routed outside the forward layers."));
        }
        if (crossLayerEdgeCount > 0)
        {
            diagnostics.Add(new OblivionNativeLayoutDiagnostic(
                "OBLIVION-NATIVE-CROSS-LAYER-EDGES-ROUTED",
                $"{crossLayerEdgeCount} edge(s) cross more than one layer and were retained as segmented routes."));
        }

        OblivionNativeLayoutMetrics metrics = new(
            orderedLayers.Count,
            weakComponentKeys.Values.Distinct(StringComparer.Ordinal).Count(),
            backEdgeCount,
            crossLayerEdgeCount,
            crossingEstimate);
        return new AutomaticLayoutPlan(
            resolvedNodes.OrderBy(node => node.Id, StringComparer.Ordinal).ToArray(),
            routeKinds,
            metrics,
            diagnostics);
    }

    private static IReadOnlyList<string[]> FindStronglyConnectedComponents(
        IEnumerable<string> nodeIds,
        IReadOnlyDictionary<string, string[]> outgoing)
    {
        int nextIndex = 0;
        Dictionary<string, int> indexByNode = new(StringComparer.Ordinal);
        Dictionary<string, int> lowLinkByNode = new(StringComparer.Ordinal);
        Stack<string> stack = new();
        HashSet<string> onStack = new(StringComparer.Ordinal);
        List<string[]> components = [];

        void Visit(string nodeId)
        {
            indexByNode[nodeId] = nextIndex;
            lowLinkByNode[nodeId] = nextIndex;
            nextIndex++;
            stack.Push(nodeId);
            onStack.Add(nodeId);

            foreach (string targetId in outgoing[nodeId])
            {
                if (!indexByNode.ContainsKey(targetId))
                {
                    Visit(targetId);
                    lowLinkByNode[nodeId] = Math.Min(lowLinkByNode[nodeId], lowLinkByNode[targetId]);
                }
                else if (onStack.Contains(targetId))
                {
                    lowLinkByNode[nodeId] = Math.Min(lowLinkByNode[nodeId], indexByNode[targetId]);
                }
            }

            if (lowLinkByNode[nodeId] != indexByNode[nodeId])
            {
                return;
            }

            List<string> component = [];
            while (stack.Count > 0)
            {
                string member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
                if (member == nodeId)
                {
                    break;
                }
            }
            components.Add(component.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }

        foreach (string nodeId in nodeIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!indexByNode.ContainsKey(nodeId))
            {
                Visit(nodeId);
            }
        }

        return components
            .OrderBy(component => component[0], StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<int, int> RankComponents(
        IReadOnlyList<string[]> components,
        IReadOnlyDictionary<string, int> componentByNode,
        IReadOnlyList<DiagramEdge> edges)
    {
        Dictionary<int, SortedSet<int>> successors = Enumerable.Range(0, components.Count)
            .ToDictionary(index => index, _ => new SortedSet<int>());
        int[] inDegrees = new int[components.Count];
        foreach (DiagramEdge edge in edges)
        {
            int source = componentByNode[edge.From];
            int target = componentByNode[edge.To];
            if (source != target && successors[source].Add(target))
            {
                inDegrees[target]++;
            }
        }

        SortedSet<(string Key, int Index)> ready = new();
        for (int index = 0; index < components.Count; index++)
        {
            if (inDegrees[index] == 0)
            {
                ready.Add((components[index][0], index));
            }
        }

        Dictionary<int, int> ranks = Enumerable.Range(0, components.Count)
            .ToDictionary(index => index, _ => 0);
        while (ready.Count > 0)
        {
            (string _, int source) = ready.Min;
            ready.Remove(ready.Min);
            foreach (int target in successors[source])
            {
                ranks[target] = Math.Max(ranks[target], ranks[source] + components[source].Length);
                inDegrees[target]--;
                if (inDegrees[target] == 0)
                {
                    ready.Add((components[target][0], target));
                }
            }
        }
        return ranks;
    }

    private static Dictionary<string, string> FindWeakComponentKeys(Diagram diagram)
    {
        Dictionary<string, SortedSet<string>> neighbors = diagram.Nodes.ToDictionary(
            node => node.Id,
            _ => new SortedSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (DiagramEdge edge in diagram.Edges)
        {
            neighbors[edge.From].Add(edge.To);
            neighbors[edge.To].Add(edge.From);
        }

        Dictionary<string, string> keys = new(StringComparer.Ordinal);
        foreach (DiagramNode node in diagram.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            if (keys.ContainsKey(node.Id))
            {
                continue;
            }

            List<string> members = [];
            Queue<string> pending = new();
            pending.Enqueue(node.Id);
            keys[node.Id] = node.Id;
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                members.Add(current);
                foreach (string neighbor in neighbors[current])
                {
                    if (keys.TryAdd(neighbor, node.Id))
                    {
                        pending.Enqueue(neighbor);
                    }
                }
            }

            string stableKey = members.Min(StringComparer.Ordinal)!;
            foreach (string member in members)
            {
                keys[member] = stableKey;
            }
        }
        return keys;
    }

    private static Dictionary<int, List<string>> CreateInitialLayerOrder(
        Diagram diagram,
        IReadOnlyDictionary<string, int> ranks,
        IReadOnlyDictionary<string, string> weakComponentKeys)
    {
        return diagram.Nodes
            .GroupBy(node => ranks[node.Id])
            .OrderBy(group => group.Key)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(node => weakComponentKeys[node.Id], StringComparer.Ordinal)
                    .ThenBy(node => node.Id, StringComparer.Ordinal)
                    .Select(node => node.Id)
                    .ToList());
    }

    private static void ImproveLayerOrder(
        Diagram diagram,
        IReadOnlyDictionary<string, int> ranks,
        Dictionary<int, List<string>> layers)
    {
        Dictionary<string, string[]> incoming = diagram.Nodes.ToDictionary(
            node => node.Id,
            node => diagram.Edges
                .Where(edge => edge.To == node.Id && ranks[edge.From] < ranks[node.Id])
                .Select(edge => edge.From)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        Dictionary<string, string[]> outgoing = diagram.Nodes.ToDictionary(
            node => node.Id,
            node => diagram.Edges
                .Where(edge => edge.From == node.Id && ranks[edge.To] > ranks[node.Id])
                .Select(edge => edge.To)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);

        for (int sweep = 0; sweep < OrderingSweepCount; sweep++)
        {
            bool forward = sweep % 2 == 0;
            IEnumerable<int> layerRanks = forward
                ? layers.Keys.OrderBy(rank => rank)
                : layers.Keys.OrderByDescending(rank => rank);
            Dictionary<string, int> positions = CreatePositions(layers);
            foreach (int rank in layerRanks)
            {
                string[] previousOrder = layers[rank].ToArray();
                Dictionary<string, int> stablePositions = previousOrder
                    .Select((id, index) => (id, index))
                    .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
                layers[rank] = previousOrder
                    .OrderBy(id => Barycenter(forward ? incoming[id] : outgoing[id], positions, stablePositions[id]))
                    .ThenBy(id => stablePositions[id])
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .ToList();
                positions = CreatePositions(layers);
            }
        }
    }

    private static double Barycenter(
        IReadOnlyList<string> neighbors,
        IReadOnlyDictionary<string, int> positions,
        int fallback)
    {
        int[] values = neighbors
            .Where(positions.ContainsKey)
            .Select(id => positions[id])
            .ToArray();
        return values.Length == 0 ? fallback : values.Average();
    }

    private static Dictionary<string, int> CreatePositions(
        IReadOnlyDictionary<int, List<string>> layers)
    {
        Dictionary<string, int> positions = new(StringComparer.Ordinal);
        foreach ((int _, List<string> nodeIds) in layers)
        {
            for (int index = 0; index < nodeIds.Count; index++)
            {
                positions[nodeIds[index]] = index;
            }
        }
        return positions;
    }

    private static IReadOnlyList<OblivionResolvedDiagramNode> PlaceNodes(
        Diagram diagram,
        IReadOnlyDictionary<string, int> ranks,
        IReadOnlyDictionary<int, List<string>> layers,
        IReadOnlyDictionary<string, string> weakComponentKeys,
        IReadOnlyDictionary<string, DiagramNode> nodesById,
        Func<string, (double Width, double Height)> measureNode,
        Func<DiagramNode, double, double, double, double, OblivionResolvedDiagramNode> createNode)
    {
        Dictionary<string, (double Width, double Height)> sizes = diagram.Nodes.ToDictionary(
            node => node.Id,
            node => measureNode(node.Label),
            StringComparer.Ordinal);
        Dictionary<int, double> layerDepths = layers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Max(id => diagram.Direction == DiagramDirection.LeftRight
                ? sizes[id].Width
                : sizes[id].Height));
        Dictionary<int, double> layerOffsets = [];
        double nextLayerOffset = OuterMargin;
        foreach (int rank in layers.Keys.OrderBy(rank => rank))
        {
            layerOffsets[rank] = nextLayerOffset;
            nextLayerOffset += layerDepths[rank] + LayerGap;
        }

        Dictionary<int, double> layerBreadths = layers.ToDictionary(
            pair => pair.Key,
            pair => MeasureLayerBreadth(
                pair.Value,
                sizes,
                weakComponentKeys,
                diagram.Direction));
        double maximumBreadth = layerBreadths.Values.Max();
        List<OblivionResolvedDiagramNode> resolved = [];
        foreach (int rank in layers.Keys.OrderBy(rank => rank))
        {
            List<string> nodeIds = layers[rank];
            double breadthOffset = OuterMargin + ((maximumBreadth - layerBreadths[rank]) / 2);
            string? previousComponent = null;
            foreach (string nodeId in nodeIds)
            {
                if (previousComponent is not null && previousComponent != weakComponentKeys[nodeId])
                {
                    breadthOffset += ComponentGap;
                }

                DiagramNode node = nodesById[nodeId];
                (double width, double height) = sizes[nodeId];
                double x = diagram.Direction == DiagramDirection.LeftRight
                    ? layerOffsets[ranks[nodeId]]
                    : breadthOffset;
                double y = diagram.Direction == DiagramDirection.LeftRight
                    ? breadthOffset
                    : layerOffsets[ranks[nodeId]];
                resolved.Add(createNode(node, x, y, width, height));
                breadthOffset += (diagram.Direction == DiagramDirection.LeftRight ? height : width) + NodeGap;
                previousComponent = weakComponentKeys[nodeId];
            }
        }
        return resolved;
    }

    private static double MeasureLayerBreadth(
        IReadOnlyList<string> nodeIds,
        IReadOnlyDictionary<string, (double Width, double Height)> sizes,
        IReadOnlyDictionary<string, string> weakComponentKeys,
        DiagramDirection direction)
    {
        double breadth = 0;
        string? previousComponent = null;
        for (int index = 0; index < nodeIds.Count; index++)
        {
            string nodeId = nodeIds[index];
            if (previousComponent is not null && previousComponent != weakComponentKeys[nodeId])
            {
                breadth += ComponentGap;
            }
            breadth += direction == DiagramDirection.LeftRight
                ? sizes[nodeId].Height
                : sizes[nodeId].Width;
            if (index + 1 < nodeIds.Count)
            {
                breadth += NodeGap;
            }
            previousComponent = weakComponentKeys[nodeId];
        }
        return breadth;
    }

    private static int EstimateAdjacentLayerCrossings(
        IReadOnlyList<DiagramEdge> edges,
        IReadOnlyDictionary<string, int> ranks,
        IReadOnlyDictionary<string, int> positions)
    {
        DiagramEdge[] candidates = edges
            .Where(edge => ranks[edge.To] - ranks[edge.From] == 1)
            .ToArray();
        int crossings = 0;
        for (int firstIndex = 0; firstIndex < candidates.Length; firstIndex++)
        {
            DiagramEdge first = candidates[firstIndex];
            for (int secondIndex = firstIndex + 1; secondIndex < candidates.Length; secondIndex++)
            {
                DiagramEdge second = candidates[secondIndex];
                if (ranks[first.From] != ranks[second.From] ||
                    first.From == second.From ||
                    first.To == second.To)
                {
                    continue;
                }

                int sourceDelta = positions[first.From].CompareTo(positions[second.From]);
                int targetDelta = positions[first.To].CompareTo(positions[second.To]);
                if (sourceDelta != 0 && targetDelta != 0 && sourceDelta != targetDelta)
                {
                    crossings++;
                }
            }
        }
        return crossings;
    }

    private static string EdgeKey(DiagramEdge edge, int index)
    {
        return edge.SemanticIdentity ??
            $"edge:{index.ToString(CultureInfo.InvariantCulture)}:{edge.From}:{edge.To}";
    }
}
