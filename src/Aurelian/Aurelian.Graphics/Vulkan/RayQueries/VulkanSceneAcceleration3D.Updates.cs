using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.RayQueries;

internal sealed unsafe partial class VulkanSceneAcceleration3D
{
    private const BuildAccelerationStructureFlagsKHR TopLevelFlags =
        BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr | BuildAccelerationStructureFlagsKHR.AllowUpdateBitKhr;
    private AurelianVulkanBuffer[] instanceInputs = [];
    private AurelianVulkanBuffer[] instanceMetadata = [];
    private AurelianVulkanBuffer[] proceduralData = [];
    private ulong updateScratchAddress;
    private bool updatePending;
    private bool streamingStorage;
    private int preparedSlot;
    private int topLevelUpdates;
    private int snapshotAllocations;
    private int synchronousBuildSubmissions;
    private long instanceUploadBytes;

    internal static bool CanUpdate(NativeInstanceSet3D? prior, NativeInstanceSet3D next,
        SceneAccelerationUpdateMode3D mode)
    {
        return mode == SceneAccelerationUpdateMode3D.Automatic && next.Continues(prior);
    }

    public bool RequiresRebuild(NativeInstanceSet3D instances, SceneAccelerationUpdateMode3D mode)
    {
        return !Matches(instances) && (!streamingStorage || !CanUpdate(previous, instances, mode));
    }

    public void Prepare(NativeInstanceSet3D instances, int slot, SceneAccelerationUpdateMode3D mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (!Matches(instances))
        {
            if (streamingStorage && CanUpdate(previous, instances, mode))
            {
                UploadSnapshot(instances, slot, initializeAllSlots: false);
                previous = instances;
                updatePending = true;
            }
            else
            {
                Rebuild(instances, slot, mode == SceneAccelerationUpdateMode3D.Automatic);
            }
        }
        preparedSlot = slot;
        Instances = instanceMetadata[slot];
        Procedurals = proceduralData[slot];
    }

    public void RecordUpdate(VulkanCommandBufferLease command)
    {
        if (!updatePending)
        {
            return;
        }
        // The TLAS and scratch are shared across queued frames. Finish earlier
        // ray reads and builds before updating in place on the same graphics queue.
        Barrier(command.CommandBuffer,
            AccessFlags.AccelerationStructureReadBitKhr | AccessFlags.AccelerationStructureWriteBitKhr,
            AccessFlags.AccelerationStructureReadBitKhr | AccessFlags.AccelerationStructureWriteBitKhr,
            PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.AccelerationStructureBuildBitKhr,
            PipelineStageFlags.AccelerationStructureBuildBitKhr);
        Barrier(command.CommandBuffer, AccessFlags.HostWriteBit,
            AccessFlags.AccelerationStructureReadBitKhr | AccessFlags.ShaderReadBit,
            PipelineStageFlags.HostBit,
            PipelineStageFlags.AccelerationStructureBuildBitKhr | PipelineStageFlags.ComputeShaderBit);
        var geometry = InstanceGeometry(instanceInputs[preparedSlot]);
        var info = new AccelerationStructureBuildGeometryInfoKHR
        {
            SType = StructureType.AccelerationStructureBuildGeometryInfoKhr,
            Type = AccelerationStructureTypeKHR.TopLevelKhr,
            Flags = TopLevelFlags,
            Mode = BuildAccelerationStructureModeKHR.UpdateKhr,
            SrcAccelerationStructure = TopLevel,
            DstAccelerationStructure = TopLevel,
            GeometryCount = 1,
            PGeometries = &geometry,
            ScratchData = new DeviceOrHostAddressKHR { DeviceAddress = updateScratchAddress },
        };
        var range = new AccelerationStructureBuildRangeInfoKHR { PrimitiveCount = (uint)previous!.Count };
        AccelerationStructureBuildRangeInfoKHR* rangePointer = &range;
        acceleration.CmdBuildAccelerationStructures(command.CommandBuffer, 1, &info, &rangePointer);
        Barrier(command.CommandBuffer, AccessFlags.AccelerationStructureWriteBitKhr,
            AccessFlags.AccelerationStructureReadBitKhr, PipelineStageFlags.AccelerationStructureBuildBitKhr,
            PipelineStageFlags.ComputeShaderBit);
        updatePending = false;
        topLevelUpdates++;
    }

    private AccelerationStructureGeometryKHR InstanceGeometry(AurelianVulkanBuffer input)
    {
        return new AccelerationStructureGeometryKHR
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
    }

    private void UploadSnapshot(NativeInstanceSet3D instances, int slot, bool initializeAllSlots)
    {
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
            {
                new RayQueryAnalytic3D(mesh.PrimitiveKind, instance.Transform, instance.RayLayer, instance.RayMask)
                    .Pack(analytic.AsSpan(index * 16, 16));
            }
        }
        for (int index = 0; index < VulkanFrameInFlightSet.Count; index++)
        {
            if (initializeAllSlots && index > 0 && ReferenceEquals(instanceInputs[index], instanceInputs[0]))
            {
                continue;
            }
            if (!initializeAllSlots && index != slot)
            {
                continue;
            }
            Write(instanceMetadata[index], metadata);
            Write(proceduralData[index], analytic);
            if (!instanceInputs[index].Write(descriptors).Success)
            {
                throw new InvalidOperationException("Retained TLAS upload failed.");
            }
            instanceUploadBytes += descriptors.Length + (metadata.Length + analytic.Length) * sizeof(float);
        }
    }
}
