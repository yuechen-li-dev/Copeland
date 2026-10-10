using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>
/// Retained six-influence DQS buffers. Compute writes the shared solid vertex ABI;
/// drawing consumes the same buffer without a CPU readback or mesh rebuild.
/// </summary>
public sealed unsafe class VulkanSkinning3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly AurelianVulkanBuffer palette;
    private readonly AurelianVulkanBuffer pose;
    private readonly int jointCount;
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private DescriptorSet descriptorSet;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    private bool disposed;

    public NativeGpuGeometry3D Geometry { get; }
    public int DispatchCount { get; private set; }

    public VulkanSkinning3D(AurelianVulkanPlant plant, byte[] spirv, ReadOnlySpan<float> rest,
        int vertexCount, int jointCount)
    {
        if (vertexCount <= 0 || vertexCount > 1_000_000 || vertexCount % 3 != 0 ||
            rest.Length != vertexCount * 24 || jointCount is < 1 or > 512 ||
            spirv.Length < 20 || spirv.Length % 4 != 0)
        {
            throw new ArgumentException("Skinning needs bounded triangles, 24-float rest vertices and aligned SPIR-V.");
        }
        for (int index = 0; index < rest.Length; index++)
        {
            if (!float.IsFinite(rest[index])) throw new ArgumentException("Nonfinite skinning rest data.");
        }
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            double sum = 0;
            for (int influence = 0; influence < 6; influence++)
            {
                int offset = vertex * 24 + 6 + influence * 2;
                float joint = rest[offset];
                float weight = rest[offset + 1];
                if (joint < 0 || joint >= jointCount || joint != MathF.Truncate(joint) || weight < 0)
                    throw new ArgumentException("Invalid skinning joint or weight.");
                sum += weight;
            }
            if (Math.Abs(sum - 1) > .0001) throw new ArgumentException("Skinning weights must sum to one.");
        }
        this.plant = plant;
        this.jointCount = jointCount;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        try
        {
            var source = Buffer((ulong)rest.Length * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Write(source, rest);
            palette = Buffer((ulong)jointCount * 32, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            pose = Buffer(19 * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            // Host-visible output allows explicit qualification reads. Normal presentation never reads it.
            var output = Buffer((ulong)vertexCount * 40, VulkanBufferUsage.Storage | VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferSource,
                VulkanMemoryUsage.GpuToCpu);
            Geometry = new(output, (uint)vertexCount);
            CreatePipeline(spirv, [source, palette, pose, output]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Deform(ReadOnlySpan<float> jointPalette, float leftCorrection, float rightCorrection,
        Matrix4x4 world)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (jointPalette.Length != jointCount * 8 || !float.IsFinite(leftCorrection) ||
            !float.IsFinite(rightCorrection) || leftCorrection is < 0 or > 1 || rightCorrection is < 0 or > 1)
            throw new ArgumentException("Invalid palette length or corrective activation.");
        for (int index = 0; index < jointCount; index++)
        {
            var real = new Quaternion(jointPalette[index * 8], jointPalette[index * 8 + 1],
                jointPalette[index * 8 + 2], jointPalette[index * 8 + 3]);
            if (!float.IsFinite(real.LengthSquared()) || MathF.Abs(real.LengthSquared() - 1) > .0001)
                throw new ArgumentException("Skinning palette requires unit real quaternions.");
        }
        foreach (float value in jointPalette)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Nonfinite skinning palette.");
        }
        float[] header = [Geometry.VertexCount, leftCorrection, rightCorrection,
            world.M11, world.M12, world.M13, world.M14, world.M21, world.M22, world.M23, world.M24,
            world.M31, world.M32, world.M33, world.M34, world.M41, world.M42, world.M43, world.M44];
        var basisX = new Vector3(world.M11, world.M12, world.M13);
        var basisY = new Vector3(world.M21, world.M22, world.M23);
        var basisZ = new Vector3(world.M31, world.M32, world.M33);
        if (header.Any(value => !float.IsFinite(value)) ||
            MathF.Abs(basisX.LengthSquared() - 1) > .0001f ||
            MathF.Abs(basisY.LengthSquared() - 1) > .0001f ||
            MathF.Abs(basisZ.LengthSquared() - 1) > .0001f ||
            MathF.Abs(Vector3.Dot(basisX, basisY)) > .0001f ||
            MathF.Abs(Vector3.Dot(basisX, basisZ)) > .0001f ||
            MathF.Abs(Vector3.Dot(basisY, basisZ)) > .0001f ||
            world.M14 != 0 || world.M24 != 0 || world.M34 != 0 || world.M44 != 1 || world.GetDeterminant() <= 0)
            throw new ArgumentException("Character presentation requires a finite rigid world transform.");
        Write(palette, jointPalette);
        Write(pose, header);
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Skinning command begin failed.");
        Barrier(command.CommandBuffer, AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute,
            pipelineLayout, 0, 1, &set, 0, null);
        plant.Vk.CmdDispatch(command.CommandBuffer, (Geometry.VertexCount + 63) / 64, 1, 1);
        Barrier(command.CommandBuffer, AccessFlags.ShaderWriteBit,
            AccessFlags.VertexAttributeReadBit | AccessFlags.HostReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.VertexInputBit | PipelineStageFlags.HostBit);
        Require(command.End().Success, "Skinning command end failed.");
        var result = submitter.Submit(new(command, DebugName: "humanoid.skinning"));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        DispatchCount++;
    }

    public Native3DVertex[] ReadVertices()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return MemoryMarshal.Cast<byte, Native3DVertex>(Geometry.Buffer.ReadBytes(checked((int)Geometry.VertexCount * 40))).ToArray();
    }

    private void CreatePipeline(byte[] spirv, AurelianVulkanBuffer[] resources)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[4];
        for (uint index = 0; index < 4; index++)
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        var layout = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings
        };
        fixed (DescriptorSetLayout* value = &setLayout) Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, 4);
        var pool = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &size
        };
        fixed (DescriptorPool* value = &descriptorPool) Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        DescriptorSetLayout descriptorLayout = setLayout;
        var allocate = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &descriptorLayout
        };
        fixed (DescriptorSet* value = &descriptorSet) Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, value));
        DescriptorBufferInfo* infos = stackalloc DescriptorBufferInfo[4];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[4];
        for (int index = 0; index < 4; index++)
        {
            infos[index] = new(resources[index].NativeBuffer, 0, resources[index].SizeBytes);
            writes[index] = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = (uint)index,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &infos[index]
            };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, 4, writes, 0, null);
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorLayout
        };
        fixed (PipelineLayout* value = &pipelineLayout) Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, value));
        ShaderModule module;
        fixed (byte* bytes = spirv)
        {
            var moduleInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)bytes
            };
            Check(plant.Vk.CreateShaderModule(plant.Device, &moduleInfo, null, &module));
        }
        nint entry = SilkMarshal.StringToPtr("SkinMain");
        try
        {
            var create = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Layout = pipelineLayout,
                Stage = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = module,
                    PName = (byte*)entry
                }
            };
            fixed (Pipeline* value = &pipeline) Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, value));
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }

    private AurelianVulkanBuffer Buffer(ulong bytes, VulkanBufferUsage usage, VulkanMemoryUsage memory)
    {
        var result = VulkanBufferFactory.Create(plant, allocator,
            new(plant.Context.Id, bytes, usage, memory, "humanoid", MapOnCreate: true));
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        buffers.Add(result.Buffer!);
        return result.Buffer!;
    }

    private void Barrier(CommandBuffer command, AccessFlags source, AccessFlags destination,
        PipelineStageFlags sourceStage, PipelineStageFlags destinationStage)
    {
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = source,
            DstAccessMask = destination
        };
        plant.Vk.CmdPipelineBarrier(command, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Write(AurelianVulkanBuffer buffer, ReadOnlySpan<float> words) =>
        Require(buffer.Write(MemoryMarshal.AsBytes(words)).Success, "Skinning upload failed.");

    private static void Check(Result result) => Require(result == Result.Success, "Skinning Vulkan operation failed: " + result);
    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        if (pipeline.Handle != 0) plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        if (pipelineLayout.Handle != 0) plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
        if (descriptorPool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        if (setLayout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        foreach (var buffer in buffers.AsEnumerable().Reverse()) buffer.Dispose();
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
