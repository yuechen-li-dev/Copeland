using System.Numerics;
using System.Runtime.InteropServices;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>World-space triangle-list vertex. Projection and lighting execute on the GPU.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Native3DVertex(Vector3 Position, Vector3 Normal, Vector4 Color);

public sealed record Native3DFrameResult(int TriangleCount, byte[]? Pixels, string? PixelSha256);
