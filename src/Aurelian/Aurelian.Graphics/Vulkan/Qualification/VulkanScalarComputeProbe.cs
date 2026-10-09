using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Sync;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Qualification;

// Qualification-only dispatch. Production compute runners retain their own
// resource contracts; this witness owns exactly two scalar storage buffers.
public sealed unsafe class VulkanScalarComputeProbe : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commands;
    private readonly VulkanCommandSubmitter submitter;
    private readonly List<AurelianVulkanBuffer> buffers = [];
    private readonly AurelianVulkanBuffer output;
    private readonly int valueCount;
    private DescriptorSetLayout setLayout;
    private DescriptorPool descriptorPool;
    private DescriptorSet descriptorSet;
    private PipelineLayout pipelineLayout;
    private Pipeline pipeline;
    private bool disposed;

    public VulkanScalarComputeProbe(AurelianVulkanPlant plant, byte[] spirv, string entryName, ReadOnlySpan<float> input)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(spirv);
        if (input.Length is < 1 or > 4096 || spirv.Length < 20 || spirv.Length % 4 != 0 || string.IsNullOrWhiteSpace(entryName))
        {
            throw new ArgumentException("Scalar probe requires bounded input, an entry name and aligned SPIR-V.");
        }
        this.plant = plant;
        valueCount = input.Length;
        allocator = new(plant);
        fences = VulkanFenceBundle.Create(plant);
        commands = VulkanCommandBufferPool.Create(plant);
        submitter = new(plant, commands, fences);
        try
        {
            var source = Buffer((ulong)input.Length * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu);
            Write(source, input);
            output = Buffer((ulong)input.Length * 4, VulkanBufferUsage.Storage, VulkanMemoryUsage.GpuToCpu);
            Write(output, Enumerable.Repeat(-999.0f, input.Length).ToArray());
            CreatePipeline(spirv, entryName, [source, output]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public float[] Execute(uint workgroupsX = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (workgroupsX is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(workgroupsX));
        }
        var command = commands.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "Compute command begin failed.");
        Barrier(command.CommandBuffer, AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit);
        plant.Vk.CmdBindPipeline(command.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Compute,
            pipelineLayout, 0, 1, &set, 0, null);
        plant.Vk.CmdDispatch(command.CommandBuffer, workgroupsX, 1, 1);
        Barrier(command.CommandBuffer, AccessFlags.ShaderWriteBit, AccessFlags.HostReadBit,
            PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.HostBit);
        Require(command.End().Success, "Compute command end failed.");
        var submitted = submitter.Submit(new(command, DebugName: "vts-language-probe"));
        Require(submitted.Success, "Compute submit failed.");
        return MemoryMarshal.Cast<byte, float>(output.ReadBytes(valueCount * 4)).ToArray();
    }

    private void CreatePipeline(byte[] spirv, string entryName, AurelianVulkanBuffer[] resources)
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[2];
        for (uint index = 0; index < 2; index++)
        {
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        }
        var layout = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings,
        };
        fixed (DescriptorSetLayout* value = &setLayout)
        {
            Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        }
        var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, 2);
        var pool = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &size,
        };
        fixed (DescriptorPool* value = &descriptorPool)
        {
            Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        }
        DescriptorSetLayout descriptorLayout = setLayout;
        var allocate = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &descriptorLayout,
        };
        fixed (DescriptorSet* value = &descriptorSet)
        {
            Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, value));
        }
        DescriptorBufferInfo* infos = stackalloc DescriptorBufferInfo[2];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        for (int index = 0; index < 2; index++)
        {
            infos[index] = new(resources[index].NativeBuffer, 0, resources[index].SizeBytes);
            writes[index] = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = (uint)index,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &infos[index],
            };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, 2, writes, 0, null);
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorLayout,
        };
        fixed (PipelineLayout* value = &pipelineLayout)
        {
            Check(plant.Vk.CreatePipelineLayout(plant.Device, &layoutInfo, null, value));
        }
        ShaderModule module;
        fixed (byte* bytes = spirv)
        {
            var moduleInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)bytes,
            };
            Check(plant.Vk.CreateShaderModule(plant.Device, &moduleInfo, null, &module));
        }
        nint entry = SilkMarshal.StringToPtr(entryName);
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
                    PName = (byte*)entry,
                },
            };
            fixed (Pipeline* value = &pipeline)
            {
                Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, value));
            }
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
            new(plant.Context.Id, bytes, usage, memory, "vts-language-proof", MapOnCreate: true));
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
            DstAccessMask = destination,
        };
        plant.Vk.CmdPipelineBarrier(command, sourceStage, destinationStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private static void Write(AurelianVulkanBuffer buffer, ReadOnlySpan<float> words) =>
        Require(buffer.Write(MemoryMarshal.AsBytes(words)).Success, "Language proof upload failed.");

    private static void Check(Result result) => Require(result == Result.Success, "Language proof Vulkan operation failed: " + result);
    private static void Require(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        if (pipeline.Handle != 0)
        {
            plant.Vk.DestroyPipeline(plant.Device, pipeline, null);
        }
        if (pipelineLayout.Handle != 0)
        {
            plant.Vk.DestroyPipelineLayout(plant.Device, pipelineLayout, null);
        }
        if (descriptorPool.Handle != 0)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        }
        if (setLayout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        }
        foreach (var buffer in buffers.AsEnumerable().Reverse())
        {
            buffer.Dispose();
        }
        submitter.Dispose();
        commands.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }
}
