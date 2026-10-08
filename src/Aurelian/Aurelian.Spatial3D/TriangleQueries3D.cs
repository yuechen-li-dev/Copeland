using System.Numerics;

namespace Aurelian.Spatial3D;

/// <summary>Adapted from Aetheris BrepPicker and Reconstruction/TriangleBvh; metre tolerances,
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

    internal static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 >= d3 && d5 >= d6) return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
        float inverse = 1 / (va + vb + vc);
        return a + ab * (vb * inverse) + ac * (vc * inverse);
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
