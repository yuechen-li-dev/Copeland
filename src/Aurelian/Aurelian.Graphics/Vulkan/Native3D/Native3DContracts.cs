using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>World-space triangle-list vertex. Projection and lighting execute on the GPU.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Native3DVertex(Vector3 Position, Vector3 Normal, Vector4 Color);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct NativeModel3DVertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 Uv, Vector4 Tangent);
public sealed record NativeModel3DBatch(NativeModel3DVertex[] Vertices, ModelMaterial Material)
{
    public string? TemporalIdentity { get; init; }
}
public sealed record Native3DScene(Native3DVertex[] Geometry, IReadOnlyList<NativeModel3DBatch> Models)
{
    /// <summary>Change when vertex correspondence changes; transforms and animation retain this key.</summary>
    public string? TemporalRevision { get; init; }
    /// <summary>Change when opaque shadow casters move or deform. Scene composition supplies transforms automatically.</summary>
    public string? LightingRevision { get; init; }
}

public sealed record Native3DFrameResult(int TriangleCount, byte[]? Pixels, string? PixelSha256)
{
    public IReadOnlyList<Native3DGpuPassTime> GpuPassTimes { get; init; } = [];
}
