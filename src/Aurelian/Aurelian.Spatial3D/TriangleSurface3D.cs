using System.Numerics;

namespace Aurelian.Spatial3D;

public readonly record struct TriangleProximity3D(Vector3 Point, Vector3 Barycentric);

/// <summary>Game-facing projection of the shared Aetheris triangle proximity query.</summary>
public static class TriangleSurface3D
{
    public static TriangleProximity3D ClosestPoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        var result = Aetheris.Kernel.Core.Math.TriangleSurface3D.ClosestPoint(point, a, b, c);
        return new(result.Point, result.Barycentric);
    }
}
