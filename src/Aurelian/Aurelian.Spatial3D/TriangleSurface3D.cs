using System.Numerics;

namespace Aurelian.Spatial3D;

public readonly record struct TriangleProximity3D(Vector3 Point, Vector3 Barycentric);

/// <summary>
/// Closest point on a triangle, with its barycentric coordinates. This is the textbook Voronoi-region walk
/// (Ericson, Real-Time Collision Detection, 5.1.5). Degenerate (sliver or point) triangles fall back to their longest
/// edge, so contacts never divide by a near-zero area.
/// </summary>
public static class TriangleSurface3D
{
    public static TriangleProximity3D ClosestPoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        float abLength = Vector3.Dot(ab, ab);
        float acLength = Vector3.Dot(ac, ac);
        float abDotAc = Vector3.Dot(ab, ac);
        float area = abLength * acLength - abDotAc * abDotAc;
        if (area <= 1e-8f * abLength * acLength || abLength * acLength < 1e-24f)
        {
            return LongestEdge(point, a, b, c);
        }
        Vector3 closest = ClosestOnTriangle(point, a, b, c);
        Vector3 offset = closest - a;
        float alongAb = Vector3.Dot(offset, ab);
        float alongAc = Vector3.Dot(offset, ac);
        float v = (acLength * alongAb - abDotAc * alongAc) / area;
        float w = (abLength * alongAc - abDotAc * alongAb) / area;
        return new(closest, new Vector3(1 - v - w, v, w));
    }

    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return a;
        }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return b;
        }

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            return a + ab * (d1 / (d1 - d3));
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return c;
        }

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            return a + ac * (d2 / (d2 - d6));
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        }

        float denominator = 1 / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    private static TriangleProximity3D LongestEdge(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        float ab = Vector3.DistanceSquared(a, b);
        float ac = Vector3.DistanceSquared(a, c);
        float bc = Vector3.DistanceSquared(b, c);
        if (ab >= ac && ab >= bc)
        {
            return Segment(point, a, b, Vector3.UnitX, Vector3.UnitY);
        }
        if (ac >= bc)
        {
            return Segment(point, a, c, Vector3.UnitX, Vector3.UnitZ);
        }
        return Segment(point, b, c, Vector3.UnitY, Vector3.UnitZ);
    }

    private static TriangleProximity3D Segment(Vector3 point, Vector3 start, Vector3 end, Vector3 startWeight, Vector3 endWeight)
    {
        Vector3 direction = end - start;
        float length = Vector3.Dot(direction, direction);
        float t = length > 0 ? Math.Clamp(Vector3.Dot(point - start, direction) / length, 0, 1) : 0;
        return new(start + direction * t, startWeight * (1 - t) + endWeight * t);
    }
}
