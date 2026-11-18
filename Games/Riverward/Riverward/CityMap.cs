using Aurelian.Strategy;

namespace Riverward;

internal static class CityMap
{
    internal const float Radius = 3.5f;
    internal static readonly HexCell FirstHomes = new(-2, 0);
    internal static readonly HexCell SecondHomes = new(-2, 1);
    internal static readonly HexCell FirstWorkshop = new(1, 0);
    internal static readonly HexCell TownHall = new(-1, -1);
    internal static readonly HexEdge Bridge = new(new HexCell(0, 0).Corner(0), new HexCell(0, 0).Corner(2));

    internal static readonly HexEdge[] InitialStreets =
    [
        new HexCell(-1, 0).Edge(0), new HexCell(-1, 0).Edge(1), new HexCell(-1, 0).Edge(2),
        FirstWorkshop.Edge(2),
    ];

    internal static readonly HexCell[] Cells = Enumerable.Range(-5, 11)
        .SelectMany(r => Enumerable.Range(-7, 15).Select(x => new HexCell(x - (int)Math.Floor(r / 2.0), r)))
        .ToArray();

    private static readonly HashSet<HexCell> Map = [.. Cells];
    private static readonly Dictionary<HexEdge, HexCell[]> StreetFrontage = Cells
        .Where(cell => !IsWater(cell))
        .SelectMany(cell => Enumerable.Range(0, HexCell.SideCount).Select(side => (cell.Edge(side), cell)))
        .GroupBy(pair => pair.Item1)
        .ToDictionary(group => group.Key, group => group.Select(pair => pair.cell).ToArray());

    internal static bool Contains(HexCell cell) => Map.Contains(cell);
    internal static bool IsWater(HexCell cell) => cell.Q == 0;
    internal static bool CanBuild(HexCell cell) => Contains(cell) && !IsWater(cell) && cell != TownHall;
    internal static bool CanLayStreet(HexEdge edge) => StreetFrontage.ContainsKey(edge);
    internal static IReadOnlyList<HexCell> Frontage(HexEdge edge) => StreetFrontage[edge];

    internal static HexCell? WaterAtShore(HexCell cell)
    {
        if (Math.Abs(cell.Q) != 1 || !Contains(cell))
        {
            return null;
        }
        HexCell water = cell.Neighbor(cell.Q < 0 ? 5 : 2);
        return Contains(water) ? water : null;
    }
}
