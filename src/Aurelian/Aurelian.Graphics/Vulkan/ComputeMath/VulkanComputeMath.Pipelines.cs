using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed unsafe partial class VulkanComputeMath
{
    private static readonly IReadOnlyDictionary<string, string> Entries = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sgemm.v.ts"] = "SgemmMain",
        ["StreamingLinear.v.ts"] = "StreamingLinearMain",
        ["CooperativeSgemm.v.ts"] = "CooperativeSgemmMain",
        ["RowReduction.v.ts"] = "ReductionMain",
        ["Normalization.v.ts"] = "NormalizationMain",
        ["Activation.v.ts"] = "ActivationMain",
        ["Rope.v.ts"] = "RopeMain",
        ["FftBitReverse.v.ts"] = "BitReverseMain",
        ["FftButterfly.v.ts"] = "ButterflyMain",
        ["Im2Col.v.ts"] = "Im2ColMain",
        ["AttentionMask.v.ts"] = "MaskMain",
    };

    private void CreateLayouts()
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[4];
        for (uint index = 0; index < 4; index++)
        {
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        }
        var layout = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 4, PBindings = bindings,
        };
        fixed (DescriptorSetLayout* value = &setLayout)
        {
            Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        }
        var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, (uint)MaximumDispatches * 4);
        var pool = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo, MaxSets = (uint)MaximumDispatches,
            PoolSizeCount = 1, PPoolSizes = &size,
        };
        fixed (DescriptorPool* value = &descriptorPool)
        {
            Check(plant.Vk.CreateDescriptorPool(plant.Device, &pool, null, value));
        }
        var descriptorLayout = setLayout;
        var pipelineInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &descriptorLayout,
        };
        fixed (PipelineLayout* value = &pipelineLayout)
        {
            Check(plant.Vk.CreatePipelineLayout(plant.Device, &pipelineInfo, null, value));
        }
    }

    private DispatchSlot CreateSlot()
    {
        var descriptorLayout = setLayout;
        var allocation = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = descriptorPool,
            DescriptorSetCount = 1, PSetLayouts = &descriptorLayout,
        };
        DescriptorSet set;
        Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocation, &set));
        return new(set, Buffer(64, VulkanBufferUsage.Storage, VulkanMemoryUsage.CpuToGpu));
    }

    private void Configure(DispatchSlot slot, ComputeMathDispatch dispatch)
    {
        KernelParameters[] parameters = [dispatch.Parameters];
        Require(slot.Parameters.Write(MemoryMarshal.AsBytes(parameters.AsSpan())).Success, "Kernel parameter upload failed.");
        AurelianVulkanBuffer[] resources = [dispatch.Input.Buffer, dispatch.Auxiliary.Buffer, dispatch.Output.Buffer, slot.Parameters];
        DescriptorBufferInfo* infos = stackalloc DescriptorBufferInfo[4];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[4];
        for (int index = 0; index < 4; index++)
        {
            infos[index] = new(resources[index].NativeBuffer, 0, resources[index].SizeBytes);
            writes[index] = new()
            {
                SType = StructureType.WriteDescriptorSet, DstSet = slot.Set, DstBinding = (uint)index,
                DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &infos[index],
            };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, 4, writes, 0, null);
    }

    private void EnsurePipeline(string shader)
    {
        if (pipelines.ContainsKey(shader))
        {
            return;
        }
        byte[] spirv = compile(shader);
        ComputeMathBatch.Require(spirv.Length >= 20 && spirv.Length % 4 == 0 && Entries.ContainsKey(shader),
            "A math kernel requires a registered entry and aligned SPIR-V.");
        ShaderModule module;
        fixed (byte* bytes = spirv)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)bytes,
            };
            Check(plant.Vk.CreateShaderModule(plant.Device, &info, null, &module));
        }
        nint entry = SilkMarshal.StringToPtr(Entries[shader]);
        try
        {
            var create = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo, Layout = pipelineLayout,
                Stage = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit,
                    Module = module, PName = (byte*)entry,
                    Flags = shader == "CooperativeSgemm.v.ts" ? PipelineShaderStageCreateFlags.RequireFullSubgroupsBit : 0,
                },
            };
            Pipeline pipeline;
            Check(plant.Vk.CreateComputePipelines(plant.Device, default, 1, &create, null, &pipeline));
            pipelines.Add(shader, pipeline);
        }
        finally
        {
            SilkMarshal.Free(entry);
            plant.Vk.DestroyShaderModule(plant.Device, module, null);
        }
    }
}
