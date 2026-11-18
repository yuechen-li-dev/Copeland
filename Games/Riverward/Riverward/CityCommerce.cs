using Aurelian.Strategy;

namespace Riverward;

internal sealed record CityCommerce(Dictionary<HexCell, int> Visitors, Dictionary<HexCell, int> Departures)
{
    internal const int DockCapacity = 12;
    private const int AttractionCapacity = 12;

    internal static CityCommerce Evaluate(CityAccess access, Dictionary<HexCell, Block> blocks, Dictionary<HexCell, int> households)
    {
        var visitors = new Dictionary<HexCell, int>();
        var departures = new Dictionary<HexCell, int>();
        var countedHomes = new HashSet<HexCell>();
        foreach (FerryStop origin in access.Stops)
        {
            HexCell[] destinations = access.Stops
                .Where(stop => stop != origin)
                .SelectMany(stop => Neighbors(stop.Land))
                .Where(cell => !Adjacent(origin.Land, cell) && blocks.TryGetValue(cell, out Block kind)
                    && kind is Block.Boardwalk or Block.FishMarket)
                .Distinct()
                .ToArray();
            if (destinations.Length == 0 || destinations.All(cell => visitors.GetValueOrDefault(cell) >= AttractionCapacity))
            {
                continue;
            }

            int demand = Neighbors(origin.Land)
                .Where(cell => access.Connected.Contains(cell) && households.ContainsKey(cell) && countedHomes.Add(cell))
                .Sum(cell => households[cell]);
            int seats = Math.Min(DockCapacity, demand);
            int sold = 0;
            while (sold < seats)
            {
                int before = sold;
                foreach (HexCell destination in destinations)
                {
                    int alreadyBooked = visitors.GetValueOrDefault(destination);
                    if (alreadyBooked < AttractionCapacity)
                    {
                        visitors[destination] = alreadyBooked + 1;
                        if (++sold == seats)
                        {
                            break;
                        }
                    }
                }
                if (sold == before)
                {
                    break;
                }
            }
            departures[origin.Land] = sold;
        }
        return new(visitors, departures);
    }

    private static IEnumerable<HexCell> Neighbors(HexCell cell) =>
        Enumerable.Range(0, HexCell.SideCount).Select(cell.Neighbor);

    private static bool Adjacent(HexCell from, HexCell to) => Neighbors(from).Contains(to);
}
