using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Four vertices describe raster coverage, never an approximation to the actual surface.</summary>
public static class NativeAnalyticPrimitives3D
{
    public static NativeMesh3D Create(string identity, AnalyticPrimitiveKind3D kind)
    {
        if (kind is < AnalyticPrimitiveKind3D.UnitSphere or > AnalyticPrimitiveKind3D.Capsule)
            throw new ArgumentOutOfRangeException(nameof(kind));
        NativeModel3DVertex Vertex(float x, float y) => new(new(x, y, 0), Vector3.UnitZ,
            Vector4.One, Vector2.Zero, new(1, 0, 0, 1));
        return new(identity, [Vertex(-1, -1), Vertex(1, -1), Vertex(1, 1), Vertex(-1, 1)],
            [0, 1, 2, 0, 2, 3], null, kind);
    }

    internal static Vector4 Bounds(AnalyticPrimitiveKind3D kind) => kind switch
    {
        AnalyticPrimitiveKind3D.UnitSphere => new(0, 0, 0, 1.0001f),
        AnalyticPrimitiveKind3D.UnitBox => new(0, 0, 0, MathF.Sqrt(3) + .0001f),
        AnalyticPrimitiveKind3D.Capsule => new(0, 0, 0, 1.0001f),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
