using Aurelian.Spatial2D;

namespace TinyFarm.Core;

/// <summary>Authored ground geometry shared by navigation, water gameplay, fluid rasterization and art.</summary>
public static class TinyFarmLandscape
{
    public const double RiverNorth = -3;
    public const double RiverSouth = 13;
    private const double BankStep = .25;
    private static readonly double[] West = BuildBank(east: false);
    private static readonly double[] East = BuildBank(east: true);

    public static WorldPolygon River { get; } = BetweenBanks();
    public static WorldPolygon WestMeadow { get; } = Meadow(east: false);
    public static WorldPolygon EastMeadow { get; } = Meadow(east: true);

    public static IReadOnlyList<WorldPath> FarmRoads { get; } =
    [
        Road("path.farm-entry", 1.05,
            [(4.5, 4.5), (5.15, 5.35), (6.15, 6.6), (8.8, 7.1), (11.8, 6.65), (14.7, 6.85), (18.5, 6.5)]),
        Road("path.farm-plots", .65, [(8.7, 7.05), (8.5, 6.25), (8.5, 5.5)]),
    ];

    public static IReadOnlyList<WorldPath> RiversideRoads { get; } =
    [
        Road("path.riverside-entry-bench-bank", .82,
            [(-1, 5.2), (1.6, 5.5), (3.1, 6.45), (4.15, 7.7), (5.9, 8.0), (7.5, 7.5), (8.0, 6.8)]),
        Road("path.riverside-bench", .60, [(4.15, 7.7), (3.65, 8.1), (3.5, 8.55)]),
    ];

    public static double WestBank(double y) => Bank(West, y);
    public static double EastBank(double y) => Bank(East, y);

    public static bool IsRiver(double x, double y)
    {
        return y >= RiverNorth && y < RiverSouth && x >= WestBank(y) && x < EastBank(y);
    }

    /// <summary>Signed bank offset in world units; zero follows the exact authored shoreline.</summary>
    public static double RiverDistance(double x, double y)
    {
        double side = Math.Max(WestBank(y) - x, x - EastBank(y));
        return Math.Max(side, Math.Max(RiverNorth - y, y - RiverSouth));
    }

    /// <summary>Conservative solver-cell overlap. Exact gameplay membership uses IsRiver.</summary>
    public static bool RiverOverlapsCell(double x, double y, double size)
    {
        if (y + size <= RiverNorth || y >= RiverSouth)
        {
            return false;
        }
        double minimumWest = WestBank(y);
        double maximumEast = EastBank(y);
        // Include every bank vertex in the interval, as well as both cell endpoints.
        double firstVertex = RiverNorth + Math.Ceiling((y - RiverNorth) / BankStep) * BankStep;
        for (double sample = firstVertex; sample < y + size; sample += BankStep)
        {
            minimumWest = Math.Min(minimumWest, WestBank(sample));
            maximumEast = Math.Max(maximumEast, EastBank(sample));
        }
        minimumWest = Math.Min(minimumWest, WestBank(y + size));
        maximumEast = Math.Max(maximumEast, EastBank(y + size));
        return x + size > minimumWest && x < maximumEast;
    }

    private static double[] BuildBank(bool east)
    {
        int count = (int)((RiverSouth - RiverNorth) / BankStep) + 1;
        var bank = new double[count];
        for (int index = 0; index < count; index++)
        {
            double y = RiverNorth + index * BankStep;
            bank[index] = east
                ? 14.6 + .65 * Math.Sin(y * .72 + 1.3) + .18 * Math.Sin(y * 1.85 + .7)
                : 9.15 + .72 * Math.Sin((y - .6) * .85) + .20 * Math.Sin(y * 2.1 + .2);
        }
        return bank;
    }

    private static double Bank(double[] values, double y)
    {
        double sample = Math.Clamp((y - RiverNorth) / BankStep, 0, values.Length - 1);
        int index = Math.Min((int)sample, values.Length - 2);
        double fraction = sample - index;
        return values[index] * (1 - fraction) + values[index + 1] * fraction;
    }

    private static WorldPolygon BetweenBanks()
    {
        var points = new List<SpatialPoint2D>();
        for (int index = 0; index < West.Length; index++)
        {
            points.Add(new(West[index], RiverNorth + index * BankStep));
        }
        for (int index = East.Length - 1; index >= 0; index--)
        {
            points.Add(new(East[index], RiverNorth + index * BankStep));
        }
        return new(points);
    }

    private static WorldPolygon Meadow(bool east)
    {
        var points = new List<SpatialPoint2D>();
        double outside = east ? 17 : -1;
        points.Add(new(outside, RiverNorth));
        for (int index = 0; index < West.Length; index++)
        {
            double y = RiverNorth + index * BankStep;
            points.Add(new(east ? East[index] : West[index], y));
        }
        points.Add(new(outside, RiverSouth));
        return new(points);
    }

    private static WorldPath Road(string id, double width, (double X, double Y)[] controls)
    {
        var points = new List<SpatialPoint3D>();
        for (int segment = 0; segment < controls.Length - 1; segment++)
        {
            var a = controls[Math.Max(0, segment - 1)];
            var b = controls[segment];
            var c = controls[segment + 1];
            var d = controls[Math.Min(controls.Length - 1, segment + 2)];
            for (int sample = 0; sample < 12; sample++)
            {
                double t = sample / 12.0;
                points.Add(new(Curve(a.X, b.X, c.X, d.X, t), Curve(a.Y, b.Y, c.Y, d.Y, t), .03));
            }
        }
        points.Add(new(controls[^1].X, controls[^1].Y, .03));
        return new(id, points, width, "PackedDirt", WorldTraversalPolicy.Walkable, "dirt-path-overlay");
    }

    private static double Curve(double a, double b, double c, double d, double t)
    {
        return .5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t
            + (-a + 3 * b - 3 * c + d) * t * t * t);
    }
}
