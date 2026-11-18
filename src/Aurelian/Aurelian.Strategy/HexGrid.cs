using System.Numerics;

namespace Aurelian.Strategy;

/// <summary>Axial coordinates for a pointy-top grid, with sides numbered clockwise from the southeast on an X-right, Z-down map.</summary>
public readonly record struct HexCell(int Q, int R)
{
    private static readonly HexCell[] Directions = [new(0, 1), new(-1, 1), new(-1, 0), new(0, -1), new(1, -1), new(1, 0)];
    private static readonly HexCorner[] Corners = [new(1, 1), new(0, 2), new(-1, 1), new(-1, -1), new(0, -2), new(1, -1)];

    public const int SideCount = 6;

    public HexCell Neighbor(int side) => new(Q + Directions[side].Q, R + Directions[side].R);

    public HexCorner Corner(int index) => new(2 * Q + R + Corners[index].X, 3 * R + Corners[index].Y);

    public HexEdge Edge(int side) => new(Corner(side), Corner((side + 1) % SideCount));

    public Vector2 Center(float radius) => new HexCorner(2 * Q + R, 3 * R).Position(radius);

    public static HexCell FromPosition(Vector2 position, float radius)
    {
        float r = position.Y / (1.5f * radius);
        float q = position.X / (MathF.Sqrt(3) * radius) - r / 2;
        float s = -q - r;
        int roundedQ = (int)MathF.Round(q);
        int roundedR = (int)MathF.Round(r);
        int roundedS = (int)MathF.Round(s);
        float errorQ = MathF.Abs(roundedQ - q);
        float errorR = MathF.Abs(roundedR - r);
        float errorS = MathF.Abs(roundedS - s);
        if (errorQ > errorR && errorQ > errorS)
        {
            roundedQ = -roundedR - roundedS;
        }
        else if (errorR > errorS)
        {
            roundedR = -roundedQ - roundedS;
        }
        return new(roundedQ, roundedR);
    }

    public int ClosestSide(Vector2 position, float radius)
    {
        float bestDistance = float.PositiveInfinity;
        int bestSide = 0;
        for (int side = 0; side < SideCount; side++)
        {
            HexEdge edge = Edge(side);
            Vector2 midpoint = (edge.A.Position(radius) + edge.B.Position(radius)) * .5f;
            float distance = Vector2.DistanceSquared(position, midpoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSide = side;
            }
        }
        return bestSide;
    }
}

/// <summary>An exact shared vertex in the hex lattice. X and Y are lattice units, not world coordinates.</summary>
public readonly record struct HexCorner(int X, int Y)
{
    public Vector2 Position(float radius) => new(X * (MathF.Sqrt(3) * .5f * radius), Y * (.5f * radius));
}

/// <summary>An undirected hex side with a stable identity shared by both adjoining cells.</summary>
public readonly record struct HexEdge
{
    [System.Text.Json.Serialization.JsonConstructor]
    public HexEdge(HexCorner a, HexCorner b)
    {
        (A, B) = a.X < b.X || (a.X == b.X && a.Y <= b.Y) ? (a, b) : (b, a);
    }

    public HexCorner A { get; }
    public HexCorner B { get; }
}
