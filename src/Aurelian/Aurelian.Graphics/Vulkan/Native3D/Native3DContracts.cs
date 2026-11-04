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
    public IReadOnlyList<SceneInstance3D> Instances { get; init; } = [];
    /// <summary>Change when vertex correspondence changes; transforms and animation retain this key.</summary>
    public string? TemporalRevision { get; init; }
    /// <summary>Change when opaque shadow casters move or deform. Scene composition supplies transforms automatically.</summary>
    public string? LightingRevision { get; init; }
    public WaterFrame3D WaterFrame { get; init; } = new();
    public PlanarWaterReflection3D? WaterReflection { get; init; }
}

public sealed record Native3DFrameResult(int TriangleCount, byte[]? Pixels, string? PixelSha256)
{
    public IReadOnlyList<Native3DGpuPassTime> GpuPassTimes { get; init; } = [];
    public Native3DRetainedStatistics? Retained { get; init; }
    public Native3DFrameCpuTimings? CpuTimings { get; init; }
    public Native3DCompletedGpuFrame? CompletedGpuFrame { get; init; }
}

public sealed record Native3DFrameCpuTimings(long Sequence, int Slot, double SlotWaitMilliseconds,
    double PrepareRecordMilliseconds, double SubmitMilliseconds);

public enum NativeRasterAdmissionMode3D
{
    Automatic,
    Rebuild,
}

public sealed record Native3DRetainedStatistics(int Instances, int DrawGroups, int UploadedMeshes,
    long GeometryUploadBytes, long InstanceUploadBytes, ulong ResidentGeometryBytes)
{
    public int CpuVisitedInstances { get; init; }
    public int TopologyAdmissions { get; init; }
    public int MotionReuses { get; init; }
    public int IndirectGroups { get; init; }
    public int MaterialDescriptorBinds { get; init; }
    public bool BindlessMaterials { get; init; }
    public bool VisibilityBuffer { get; init; }
    public bool HierarchicalZ { get; init; }
    public ulong HzbPyramidBytes { get; init; }
    public ulong VisibilityTargetBytes { get; init; }
    public ulong VisibilityAtlasBytes { get; init; }
    public ulong VisibilityCommandCopyBytes { get; init; }
    public int ResidentTextures { get; init; }
}
