using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>World-space triangle-list vertex. Projection and lighting execute on the GPU.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Native3DVertex(Vector3 Position, Vector3 Normal, Vector4 Color);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct NativeModel3DVertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 Uv, Vector4 Tangent);
public sealed record NativeModel3DBatch(NativeModel3DVertex[] Vertices, ModelMaterial Material);
public sealed record Native3DScene(Native3DVertex[] Geometry, IReadOnlyList<NativeModel3DBatch> Models);

public sealed record Native3DFrameResult(int TriangleCount, byte[]? Pixels, string? PixelSha256);
