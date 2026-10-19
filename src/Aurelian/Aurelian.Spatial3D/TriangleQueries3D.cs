using System.Numerics;

namespace Aurelian.Spatial3D;

/// <summary>Ray, point and segment queries against triangles; metre tolerances,
/// geometric contact normals and segment proximity are explicit runtime policies.</summary>
internal static class TriangleQueries3D
{
    internal static float? Ray(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 cross = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, cross);
        if (MathF.Abs(determinant) < 1e-8f)
        {
            return null;
        }
        float inverse = 1 / determinant;
        Vector3 offset = origin - a;
        float u = Vector3.Dot(offset, cross) * inverse;
        Vector3 q = Vector3.Cross(offset, edge1);
        float v = Vector3.Dot(direction, q) * inverse;
        float distance = Vector3.Dot(edge2, q) * inverse;
        if (u < -1e-6f || v < -1e-6f || u + v > 1.000001f || distance < -SpatialMath3D.Epsilon)
        {
            return null;
        }
        return MathF.Max(0, distance);
    }

    internal static Vector3 ClosestPoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        return TriangleSurface3D.ClosestPoint(point, a, b, c).Point;
    }

    internal static (Vector3 OnSegment, Vector3 OnTriangle) ClosestSegment(
        Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 direction = end - start;
        float length = direction.Length();
        if (length > 1e-8f && Ray(start, direction / length, a, b, c) is { } hit && hit <= length)
        {
            Vector3 point = start + direction * (hit / length);
            return (point, point);
        }
        var best = (OnSegment: start, OnTriangle: ClosestPoint(start, a, b, c));
        float distance = Vector3.DistanceSquared(best.OnSegment, best.OnTriangle);
        Consider(end, ClosestPoint(end, a, b, c));
        Edge(a, b);
        Edge(b, c);
        Edge(c, a);
        return best;

        void Consider(Vector3 onSegment, Vector3 onTriangle)
        {
            float candidate = Vector3.DistanceSquared(onSegment, onTriangle);
            if (candidate < distance)
            {
                distance = candidate;
                best = (onSegment, onTriangle);
            }
        }

        void Edge(Vector3 x, Vector3 y)
        {
            Vector3 edge = y - x;
            Vector3 offset = start - x;
            float aa = Vector3.Dot(direction, direction);
            float bb = Vector3.Dot(direction, edge);
            float cc = Vector3.Dot(edge, edge);
            float dd = Vector3.Dot(direction, offset);
            float ee = Vector3.Dot(edge, offset);
            float determinant = aa * cc - bb * bb;
            float s = determinant > 1e-12f ? Math.Clamp((bb * ee - cc * dd) / determinant, 0, 1) : 0;
            float t = Math.Clamp((bb * s + ee) / cc, 0, 1);
            s = aa > 1e-12f ? Math.Clamp((bb * t - dd) / aa, 0, 1) : 0;
            t = Math.Clamp((bb * s + ee) / cc, 0, 1);
            Consider(start + direction * s, x + edge * t);
        }
    }
}
