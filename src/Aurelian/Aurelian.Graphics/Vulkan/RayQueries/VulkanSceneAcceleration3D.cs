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

public sealed record SceneAccelerationStatistics3D(int MeshBuilds, int TopLevelBuilds, int Instances, long MeshTriangles, int ResidentMeshes);

/// <summary>Opaque LOD0 BLAS per immutable mesh, TLAS per retained instance snapshot.
/// Raster culling never removes off-camera ray occluders. Snapshot replacement is drained by the renderer.</summary>
internal sealed unsafe class VulkanSceneAcceleration3D : IDisposable
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
    public SceneAccelerationStatistics3D Statistics => new(meshBuilds, topLevelBuilds, previous?.Count ?? 0, meshTriangles, meshes.Count);

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

    public void Update(NativeInstanceSet3D instances)
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
        var descriptors = new byte[instances.Count * 64];
        var metadata = new float[instances.Count * 4];
        var analytic = new float[instances.Count * 16];
        for (int index = 0; index < instances.Count; index++)
        {
            SceneInstance3D instance = instances.PackedInstances[index];
            NativeMesh3D mesh = instance.Mesh;
            if (!meshes.TryGetValue(mesh, out MeshEntry? entry))
            {
                int firstBuffer = buffers.Count;
                ulong address = BuildMesh(mesh);
                entry = new(address, structures[^1], buffers.Skip(firstBuffer).ToArray());
                meshes.Add(mesh, entry);
                meshBuilds++;
            }
            float[] rows = Lighting3DUniforms.Rows(instance.Transform);
            MemoryMarshal.AsBytes(rows.AsSpan(0, 12)).CopyTo(descriptors.AsSpan(index * 64, 48));
            BitConverter.TryWriteBytes(descriptors.AsSpan(index * 64 + 48, 4), (uint)index | 0xff000000u);
            BitConverter.TryWriteBytes(descriptors.AsSpan(index * 64 + 56, 8), entry.Address);
            metadata[index * 4] = Bits((uint)mesh.PrimitiveKind);
            metadata[index * 4 + 1] = Bits(instance.RayLayer);
            metadata[index * 4 + 2] = Bits(instance.RayMask);
            metadata[index * 4 + 3] = Bits((uint)index);
            if (mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles)
                new RayQueryAnalytic3D(mesh.PrimitiveKind, instance.Transform, instance.RayLayer, instance.RayMask)
                    .Pack(analytic.AsSpan(index * 16, 16));
        }
        Instances = Snapshot((ulong)metadata.Length * 4, VulkanBufferUsage.Storage);
        Write(Instances, metadata);
        Procedurals = Snapshot((ulong)analytic.Length * 4, VulkanBufferUsage.Storage);
        Write(Procedurals, analytic);
        var input = Snapshot((ulong)descriptors.Length, VulkanBufferUsage.AccelerationStructureInput | VulkanBufferUsage.ShaderDeviceAddress);
        if (!input.Write(descriptors).Success) throw new InvalidOperationException("Retained TLAS upload failed.");
        var geometry = new AccelerationStructureGeometryKHR
        {
            SType = StructureType.AccelerationStructureGeometryKhr,
            GeometryType = GeometryTypeKHR.InstancesKhr,
            Geometry = new AccelerationStructureGeometryDataKHR
            {
                Instances = new AccelerationStructureGeometryInstancesDataKHR
                {
                    SType = StructureType.AccelerationStructureGeometryInstancesDataKhr,
                    Data = new DeviceOrHostAddressConstKHR { DeviceAddress = Address(input) },
                },
            },
        };
        int start = buffers.Count;
        Build(AccelerationStructureTypeKHR.TopLevelKhr, geometry, (uint)instances.Count);
        snapshotBuffers.AddRange(buffers.Skip(start));
        buffers.RemoveRange(start, buffers.Count - start);
        previous = instances;
        topLevelBuilds++;
    }

    private AurelianVulkanBuffer Snapshot(ulong bytes, VulkanBufferUsage usage)
    {
        var buffer = Allocate(bytes, usage, VulkanMemoryUsage.CpuToGpu);
        snapshotBuffers.Add(buffer);
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
            Flags = BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
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
        using var scratch = Allocate(sizes.BuildScratchSize + alignment, VulkanBufferUsage.Storage | VulkanBufferUsage.ShaderDeviceAddress,
            VulkanMemoryUsage.GpuOnly);
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
        Submit(command);
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
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
