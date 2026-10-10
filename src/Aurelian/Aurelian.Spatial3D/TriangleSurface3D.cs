using System.Numerics;

namespace Aurelian.Spatial3D;

public readonly record struct TriangleProximity3D(Vector3 Point, Vector3 Barycentric);

/// <summary>Shared closest-surface query. Degenerate triangles reduce to their longest edge.</summary>
public static class TriangleSurface3D
{
    public static TriangleProximity3D ClosestPoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        float aa = Vector3.Dot(ab, ab);
        float bb = Vector3.Dot(ab, ac);
        float cc = Vector3.Dot(ac, ac);
        float determinant = aa * cc - bb * bb;
        if (determinant <= 1e-8f * aa * cc || aa * cc < 1e-24f)
        {
            float bc = Vector3.DistanceSquared(b, c);
            if (aa >= cc && aa >= bc)
            {
                return Segment(point, a, b, Vector3.UnitX, Vector3.UnitY);
            }
            if (cc >= bc)
            {
                return Segment(point, a, c, Vector3.UnitX, Vector3.UnitZ);
            }
            return Segment(point, b, c, Vector3.UnitY, Vector3.UnitZ);
        }
        Vector3 closest = TriangleQueries3D.ClosestPoint(point, a, b, c);
        Vector3 delta = closest - a;
        float x = Vector3.Dot(delta, ab);
        float y = Vector3.Dot(delta, ac);
        float v = (cc * x - bb * y) / determinant;
        float w = (aa * y - bb * x) / determinant;
        return new(closest, new(1 - v - w, v, w));
    }

    private static TriangleProximity3D Segment(Vector3 point, Vector3 start, Vector3 end,
        Vector3 startWeight, Vector3 endWeight)
    {
        Vector3 edge = end - start;
        float length = edge.LengthSquared();
        float t = length > 1e-20f ? Math.Clamp(Vector3.Dot(point - start, edge) / length, 0, 1) : 0;
        return new(start + edge * t, startWeight * (1 - t) + endWeight * t);
    }
}
