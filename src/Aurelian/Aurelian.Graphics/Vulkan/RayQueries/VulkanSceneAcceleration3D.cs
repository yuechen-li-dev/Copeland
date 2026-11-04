using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Aurelian.Graphics.Vulkan.RayQueries;

public enum SceneAccelerationUpdateMode3D
{
    /// <summary>Update continuing rigid snapshots on the frame queue; rebuild other revisions.</summary>
    Automatic,
    /// <summary>Rebuild changed snapshots synchronously as a correctness and performance control.</summary>
    Rebuild,
}

public sealed record SceneAccelerationStatistics3D(int MeshBuilds, int TopLevelBuilds, int Instances, long MeshTriangles, int ResidentMeshes)
{
    public int TopLevelUpdates { get; init; }
    public int SnapshotAllocations { get; init; }
    public int SynchronousBuildSubmissions { get; init; }
    public long InstanceUploadBytes { get; init; }
}

/// <summary>Opaque LOD0 BLAS per immutable mesh and an updateable TLAS for retained snapshots.
/// Raster culling never removes off-camera ray occluders. Cold rebuilds are drained; compatible motion updates execute on the frame queue.</summary>
internal sealed unsafe partial class VulkanSceneAcceleration3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly KhrAccelerationStructure acceleration;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly List<AccelerationStructureKHR> structures = [];
    private sealed record MeshEntry(ulong Address, AccelerationStructureKHR Structure, AurelianVulkanBuffer[] Buffers);
    private readonly Dictionary<NativeMesh3D, MeshEntry> meshes = [];
    private int meshBuilds;
    private readonly List<AurelianVulkanBuffer> snapshotBuffers = [];
    private NativeInstanceSet3D? previous;
    private bool disposed;
    private long meshTriangles;
    private int topLevelBuilds;
    public AccelerationStructureKHR TopLevel { get; private set; }
    public AurelianVulkanBuffer Instances { get; private set; } = null!;
    public AurelianVulkanBuffer Procedurals { get; private set; } = null!;
    public SceneAccelerationStatistics3D Statistics => new(meshBuilds, topLevelBuilds, previous?.Count ?? 0, meshTriangles, meshes.Count)
    {
        TopLevelUpdates = topLevelUpdates,
        SnapshotAllocations = snapshotAllocations,
        SynchronousBuildSubmissions = synchronousBuildSubmissions,
        InstanceUploadBytes = instanceUploadBytes,
    };

    public VulkanSceneAcceleration3D(AurelianVulkanPlant plant)
    {
        if (!plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal)
            || !plant.Vk.TryGetDeviceExtension(plant.Instance, plant.Device, out acceleration!))
            throw new NotSupportedException("AUR-RAY-SCENE-001: Hardware ray queries are not enabled on this plant.");
        this.plant = plant;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
    }

    public bool Matches(NativeInstanceSet3D instances) => ReferenceEquals(previous, instances);

    private void Rebuild(NativeInstanceSet3D instances, int slot, bool useStreamingStorage)
    {
        if (Matches(instances)) return;
        if (instances.Any(instance => instance.Material.AlphaMask || instance.Material.AlphaBlend
            || instance.Material.Transmission > 0))
            throw new NotSupportedException("AUR-RAY-SCENE-002: Ray shadows currently admit opaque retained instances; MASK requires an opacity candidate evaluator.");
        if (TopLevel.Handle != 0) acceleration.DestroyAccelerationStructure(plant.Device, TopLevel, null);
        TopLevel = default;
        previous = null;
        foreach (var buffer in snapshotBuffers) buffer.Dispose();
        snapshotBuffers.Clear();
        updatePending = false;
        streamingStorage = useStreamingStorage;
        var active = instances.Select(instance => instance.Mesh).ToHashSet();
        foreach (NativeMesh3D removed in meshes.Keys.Where(mesh => !active.Contains(mesh)).ToArray())
        {
            MeshEntry entry = meshes[removed];
            acceleration.DestroyAccelerationStructure(plant.Device, entry.Structure, null);
            structures.Remove(entry.Structure);
            foreach (var buffer in entry.Buffers)
            {
                buffers.Remove(buffer);
                buffer.Dispose();
            }
            if (removed.PrimitiveKind == AnalyticPrimitiveKind3D.Triangles) meshTriangles -= removed.Indices.Length / 3;
            meshes.Remove(removed);
        }
        instanceMetadata = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
        proceduralData = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
        instanceInputs = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
        for (int index = 0; index < VulkanFrameInFlightSet.Count; index++)
        {
            if (!streamingStorage && index > 0)
            {
                instanceMetadata[index] = instanceMetadata[0];
                proceduralData[index] = proceduralData[0];
                instanceInputs[index] = instanceInputs[0];
                continue;
            }
            instanceMetadata[index] = Snapshot((ulong)instances.Count * 16, VulkanBufferUsage.Storage);
            proceduralData[index] = Snapshot((ulong)instances.Count * 64, VulkanBufferUsage.Storage);
            instanceInputs[index] = Snapshot((ulong)instances.Count * 64,
                VulkanBufferUsage.AccelerationStructureInput | VulkanBufferUsage.ShaderDeviceAddress);
        }
        UploadSnapshot(instances, slot, initializeAllSlots: true);
        var geometry = InstanceGeometry(instanceInputs[slot]);
        int start = buffers.Count;
        Build(AccelerationStructureTypeKHR.TopLevelKhr, geometry, (uint)instances.Count);
        snapshotAllocations += buffers.Count - start;
        snapshotBuffers.AddRange(buffers.Skip(start));
        buffers.RemoveRange(start, buffers.Count - start);
        previous = instances;
        topLevelBuilds++;
    }

    private AurelianVulkanBuffer Snapshot(ulong bytes, VulkanBufferUsage usage)
    {
        var buffer = Allocate(bytes, usage, VulkanMemoryUsage.CpuToGpu);
        snapshotBuffers.Add(buffer);
        snapshotAllocations++;
        return buffer;
    }

    private ulong BuildMesh(NativeMesh3D mesh)
    {
        if (mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles)
        {
            float radius = mesh.PrimitiveKind == AnalyticPrimitiveKind3D.Capsule ? .50001f : 1.00001f;
            float[] bounds = [-radius, -1.00001f, -radius, radius, 1.00001f, radius];
            var data = Buffer(24, VulkanBufferUsage.AccelerationStructureInput | VulkanBufferUsage.ShaderDeviceAddress, VulkanMemoryUsage.CpuToGpu);
            Write(data, bounds);
            var procedural = new AccelerationStructureGeometryKHR
            {
                SType = StructureType.AccelerationStructureGeometryKhr,
                GeometryType = GeometryTypeKHR.AabbsKhr,
                Geometry = new AccelerationStructureGeometryDataKHR
                {
                    Aabbs = new AccelerationStructureGeometryAabbsDataKHR
                    {
                        SType = StructureType.AccelerationStructureGeometryAabbsDataKhr,
                        Data = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(data) }, Stride = 24,
                    },
                },
            };
            return Build(AccelerationStructureTypeKHR.BottomLevelKhr, procedural, 1);
        }
        float[] positions = new float[mesh.Vertices.Length * 3];
        for (int index = 0; index < mesh.Vertices.Length; index++)
            WritePoint(positions, index * 3, mesh.Vertices[index].Position);
        var vertices = Buffer((ulong)positions.Length * 4, VulkanBufferUsage.AccelerationStructureInput | VulkanBufferUsage.ShaderDeviceAddress, VulkanMemoryUsage.CpuToGpu);
        Write(vertices, positions);
        var indices = Buffer((ulong)mesh.Indices.Length * 4, VulkanBufferUsage.AccelerationStructureInput | VulkanBufferUsage.ShaderDeviceAddress, VulkanMemoryUsage.CpuToGpu);
        if (!indices.Write(MemoryMarshal.AsBytes(mesh.Indices.AsSpan())).Success) throw new InvalidOperationException("Ray mesh index upload failed.");
        var geometry = new AccelerationStructureGeometryKHR
        {
            SType = StructureType.AccelerationStructureGeometryKhr,
            GeometryType = GeometryTypeKHR.TrianglesKhr,
            Geometry = new AccelerationStructureGeometryDataKHR
            {
                Triangles = new AccelerationStructureGeometryTrianglesDataKHR
                {
                    SType = StructureType.AccelerationStructureGeometryTrianglesDataKhr,
                    VertexFormat = Format.R32G32B32Sfloat,
                    VertexData = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(vertices) },
                    VertexStride = 12, MaxVertex = (uint)mesh.Vertices.Length - 1,
                    IndexType = IndexType.Uint32,
                    IndexData = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(indices) },
                },
            },
        };
        meshTriangles += mesh.Indices.Length / 3;
        return Build(AccelerationStructureTypeKHR.BottomLevelKhr, geometry, (uint)mesh.Indices.Length / 3);
    }

    private ulong Build(AccelerationStructureTypeKHR type, AccelerationStructureGeometryKHR geometry, uint count)
    {
        var info = new AccelerationStructureBuildGeometryInfoKHR
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = type,
            Flags = type == AccelerationStructureTypeKHR.TopLevelKhr
                ? TopLevelFlags : BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
            Mode = BuildAccelerationStructureModeKHR.BuildKhr,
            GeometryCount = 1,
            PGeometries = &geometry,
        };
        var sizes = new AccelerationStructureBuildSizesInfoKHR { SType = StructureType.AccelerationStructureBuildSizesInfoKhr };
        acceleration.GetAccelerationStructureBuildSizes(plant.Device, AccelerationStructureBuildTypeKHR.DeviceKhr, &info, &count, &sizes);
        var backing = Buffer(sizes.AccelerationStructureSize, VulkanBufferUsage.AccelerationStructureStorage | VulkanBufferUsage.ShaderDeviceAddress,
            VulkanMemoryUsage.GpuOnly);
        var create = new AccelerationStructureCreateInfoKHR
        {
            SType = StructureType.AccelerationStructureCreateInfoKhr,
            Buffer = backing.NativeBuffer,
            Size = sizes.AccelerationStructureSize,
            Type = type,
        };
        Check(acceleration.CreateAccelerationStructure(plant.Device, &create, null, out AccelerationStructureKHR structure));
        if (type == AccelerationStructureTypeKHR.TopLevelKhr)
            TopLevel = structure;
        else
            structures.Add(structure);
        var properties = new PhysicalDeviceAccelerationStructurePropertiesKHR { SType = StructureType.PhysicalDeviceAccelerationStructurePropertiesKhr };
        var physical = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &properties };
        plant.Vk.GetPhysicalDeviceProperties2(plant.PhysicalDevice, &physical);
        ulong alignment = properties.MinAccelerationStructureScratchOffsetAlignment;
        ulong scratchSize = sizes.BuildScratchSize;
        if (type == AccelerationStructureTypeKHR.TopLevelKhr)
        {
            scratchSize = Math.Max(scratchSize, sizes.UpdateScratchSize);
        }
        var scratch = Allocate(scratchSize + alignment, VulkanBufferUsage.Storage | VulkanBufferUsage.ShaderDeviceAddress,
            VulkanMemoryUsage.GpuOnly);
        if (type == AccelerationStructureTypeKHR.TopLevelKhr)
        {
            buffers.Add(scratch);
        }
        ulong address = Address(scratch);
        info.DstAccelerationStructure = structure;
        info.ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = (address + alignment - 1) / alignment * alignment };
        var range = new AccelerationStructureBuildRangeInfoKHR { PrimitiveCount = count };
        AccelerationStructureBuildRangeInfoKHR* rangePointer = &range;
        VulkanCommandBufferLease command = Begin();
        acceleration.CmdBuildAccelerationStructures(command.CommandBuffer, 1, &info, &rangePointer);
        Barrier(command.CommandBuffer, AccessFlags.AccelerationStructureWriteBitKhr,
            AccessFlags.AccelerationStructureReadBitKhr | AccessFlags.ShaderReadBit,
            PipelineStageFlags.AccelerationStructureBuildBitKhr,
            PipelineStageFlags.AccelerationStructureBuildBitKhr | PipelineStageFlags.ComputeShaderBit);
        try
        {
            Submit(command);
        }
        finally
        {
            if (type != AccelerationStructureTypeKHR.TopLevelKhr)
            {
                scratch.Dispose();
            }
        }
        if (type == AccelerationStructureTypeKHR.TopLevelKhr)
        {
            updateScratchAddress = info.ScratchData.DeviceAddress;
        }
        var addressInfo = new AccelerationStructureDeviceAddressInfoKHR
        {
            SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
            AccelerationStructure = structure,
        };
        ulong result = acceleration.GetAccelerationStructureDeviceAddress(plant.Device, &addressInfo);
        if (result == 0) throw new InvalidOperationException("Acceleration structure has no device address.");
        return result;
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory)
    {
        var buffer = Allocate(bytes, usage, memory);
        buffers.Add(buffer);
        return buffer;
    }

    private AurelianVulkanBuffer Allocate(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory)
    {
        var result = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes, usage, memory,
            "rayquery", MapOnCreate: memory != VulkanMemoryUsage.GpuOnly));
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));

        return result.Buffer!;
    }

    private ulong Address(AurelianVulkanBuffer buffer)
    {
        var info = new BufferDeviceAddressInfo { SType = StructureType.BufferDeviceAddressInfo, Buffer = buffer.NativeBuffer };
        ulong address = plant.Vk.GetBufferDeviceAddress(plant.Device, &info);
        if (address == 0) throw new InvalidOperationException("Buffer has no device address.");
        return address;
    }

    private VulkanCommandBufferLease Begin()
    {
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        if (!command.Begin().Success) throw new InvalidOperationException("Ray query command begin failed.");
        Barrier(command.CommandBuffer, AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit | AccessFlags.AccelerationStructureReadBitKhr,
            PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.AccelerationStructureBuildBitKhr);
        return command;
    }

    private void Submit(VulkanCommandBufferLease command)
    {
        if (!command.End().Success) throw new InvalidOperationException("Ray query command end failed.");
        var result = submitter.Submit(new(command, DebugName: "rayquery"));
        synchronousBuildSubmissions++;
        if (!result.Success) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    private void Barrier(CommandBuffer command, AccessFlags source, AccessFlags destination, PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = source, DstAccessMask = destination };
        plant.Vk.CmdPipelineBarrier(command, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Write(AurelianVulkanBuffer buffer, float[] words)
    {
        if (!buffer.Write(MemoryMarshal.AsBytes(words.AsSpan())).Success) throw new InvalidOperationException("Ray query upload failed.");
    }
    private static float Bits(uint value) => BitConverter.UInt32BitsToSingle(value);
    private static void WritePoint(float[] words, int offset, Vector3 point)
    {
        words[offset] = point.X;
        words[offset + 1] = point.Y;
        words[offset + 2] = point.Z;
    }
    private static void Check(Result result)
    {
        if (result != Result.Success) throw new InvalidOperationException("Ray query Vulkan operation failed: " + result);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (TopLevel.Handle != 0) acceleration.DestroyAccelerationStructure(plant.Device, TopLevel, null);
        foreach (var structure in structures.AsEnumerable().Reverse()) acceleration.DestroyAccelerationStructure(plant.Device, structure, null);
        foreach (var buffer in snapshotBuffers.AsEnumerable().Reverse()) buffer.Dispose();
        foreach (var buffer in buffers.AsEnumerable().Reverse()) buffer.Dispose();
        foreach (var buffer in shadingOwned) buffer.Dispose();
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
