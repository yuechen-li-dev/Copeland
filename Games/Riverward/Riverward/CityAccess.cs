using Aurelian.Strategy;

namespace Riverward;

internal readonly record struct FerryStop(HexCell Land, HexCell Water);

internal sealed record CityAccess(HashSet<HexCell> Connected, FerryStop[] Stops)
{
    internal static CityAccess Evaluate(Dictionary<HexCell, Block> blocks, HashSet<HexEdge> streets)
    {
        var junctions = new Dictionary<HexCorner, List<HexEdge>>();
        foreach (HexEdge edge in streets.Append(CityMap.Bridge))
        {
            AddEdge(edge.A, edge);
            AddEdge(edge.B, edge);
        }

        var visited = new HashSet<HexCorner>();
        var connected = new HashSet<HexCell>();
        Flood([CityMap.InitialStreets[2].A]);
        var stops = new List<FerryStop>();
        foreach ((HexCell cell, Block block) in blocks)
        {
            if (block != Block.Dock || CityMap.WaterAtShore(cell) is not { } water)
            {
                continue;
            }
            for (int side = 0; side < HexCell.SideCount; side++)
            {
                HexEdge edge = cell.Edge(side);
                if (streets.Contains(edge))
                {
                    stops.Add(new(cell, water));
                    break;
                }
            }
        }
        if (stops.Count < 2 || !stops.Any(stop => connected.Contains(stop.Land)))
        {
            return new(connected, []);
        }

        // A dock joins every paved frontage on its block. The line connects all eligible
        // stops along this river once a stop is reachable from Town Hall by land.
        Flood(stops.SelectMany(stop => StreetJunctions(stop.Land)));
        return new(connected, [.. stops.OrderBy(stop => stop.Water.R).ThenBy(stop => stop.Land.Q)]);

        IEnumerable<HexCorner> StreetJunctions(HexCell cell)
        {
            for (int side = 0; side < HexCell.SideCount; side++)
            {
                HexEdge edge = cell.Edge(side);
                if (streets.Contains(edge))
                {
                    yield return edge.A;
                }
            }
        }

        void Flood(IEnumerable<HexCorner> entrances)
        {
            var queue = new Queue<HexCorner>(entrances.Where(visited.Add));
            while (queue.TryDequeue(out HexCorner corner))
            {
                if (!junctions.TryGetValue(corner, out List<HexEdge>? branches))
                {
                    continue;
                }
                foreach (HexEdge edge in branches)
                {
                    if (edge != CityMap.Bridge)
                    {
                        connected.UnionWith(CityMap.Frontage(edge));
                    }
                    HexCorner other = edge.A == corner ? edge.B : edge.A;
                    if (visited.Add(other))
                    {
                        queue.Enqueue(other);
                    }
                }
            }
        }

        void AddEdge(HexCorner corner, HexEdge edge)
        {
            if (!junctions.TryGetValue(corner, out List<HexEdge>? edges))
            {
                edges = [];
                junctions.Add(corner, edges);
            }
            edges.Add(edge);
        }
    }
}
