using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.ComputeMath;

public sealed unsafe partial class VulkanComputeMath
{
    private uint DescriptorBindingCount => PushDispatchParameters ? 3u : 4u;

    private static readonly IReadOnlyDictionary<string, string> Entries = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["QuantizedLinear.v.ts"] = "QuantizedLinearMain",
        ["QuantizedGemv.v.ts"] = "QuantizedGemvMain",
        ["QuantizedGemv4.v.ts"] = "QuantizedGemv4Main",
        ["QuantizedQ4GateUp.v.ts"] = "QuantizedQ4GateUpMain",
        ["QuantizedQ4Tiled.v.ts"] = "QuantizedQ4TiledMain",
        ["MergeProjectionPartials.v.ts"] = "MergeProjectionPartialsMain",
        ["QuantizedGemv.Lanes16"] = "QuantizedGemv4Main",
        ["QuantizedGemv.Lanes8"] = "QuantizedGemv4Main",
        ["QuantizedGemv.InterleavedHalfScale"] = "QuantizedGemv4Main",
        ["PackQ4WeightsHalf.v.ts"] = "PackQ4WeightsHalfMain",
        ["PackQ4WeightsHalf.InterleavedHalfScale"] = "PackQ4WeightsHalfMain",
        ["QuantizedQ6K.v.ts"] = "QuantizedQ6KMain",
        ["QuantizedQ6KWord.v.ts"] = "QuantizedQ6KWordMain",
        ["QuantizedQ6KWord.Interleaved8"] = "QuantizedQ6KWordMain",
        ["ArgMax.v.ts"] = "ArgMaxMain",
        ["FusedGroupedAttention.v.ts"] = "FusedGroupedAttentionMain",
        ["FusedGroupedAttention.Keys8"] = "FusedGroupedAttentionMain",
        ["FusedGroupedAttention.SplitDecode"] = "FusedGroupedAttentionMain",
        ["MergeAttentionPartials.v.ts"] = "MergeAttentionPartialsMain",
        ["DecoderNormalize.v.ts"] = "DecoderNormalizeMain",
        ["DecoderNormalizeBounded.v.ts"] = "DecoderNormalizeBoundedMain",
        ["DecoderNormalize1536.v.ts"] = "DecoderNormalize1536Main",
        ["DecoderNormalize1536.SplitProjection"] = "DecoderNormalize1536Main",
        ["DecoderRope.v.ts"] = "DecoderRopeMain",
        ["NormalizeRotateCacheKey.v.ts"] = "NormalizeRotateCacheKeyMain",
        ["DecoderScale.v.ts"] = "DecoderScaleMain",
        ["DecoderPointwiseFp32.v.ts"] = "DecoderPointwiseFp32Main",
        ["GroupedAttention.v.ts"] = "GroupedAttentionMain",
        ["CrossAttentionScores.v.ts"] = "CrossAttentionScoresMain",
        ["CrossAttentionValues.v.ts"] = "CrossAttentionValuesMain",
        ["DecodeQkv.v.ts"] = "DecodeQkvMain",
        ["PrefillDown.v.ts"] = "PrefillDownMain",
        ["DenseGemv.v.ts"] = "DenseGemvMain",
        ["Sgemm.v.ts"] = "SgemmMain",
        ["ResidentSgemm.v.ts"] = "ResidentSgemmMain",
        ["ResidentSgemm.InterleavedHalfScale"] = "ResidentSgemmMain",
        ["BiasSilu.v.ts"] = "BiasSiluMain",
        ["TensorMap.v.ts"] = "TensorMapMain",
        ["GatherPackedHalf.v.ts"] = "GatherPackedHalfMain",
        ["VolumeMap.v.ts"] = "VolumeMapMain",
        ["CoordinateRope.v.ts"] = "CoordinateRopeMain",
        ["SparseColumns.v.ts"] = "SparseColumnsMain",
        ["SparseChannelToSpatial.v.ts"] = "SparseChannelToSpatialMain",
        ["HeadNormalize.v.ts"] = "HeadNormalizeMain",
        ["StreamingLinear.v.ts"] = "StreamingLinearMain",
        ["WideCooperativeAttention64.Keys64"] = "WideCooperativeAttention64Main",
        ["WideCooperativeAttention64.v.ts"] = "WideCooperativeAttention64Main",
        ["DenseGemv.Packed"] = "DenseGemvMain",
        ["CooperativeAttention64.v.ts"] = "CooperativeAttention64Main",
        ["CooperativeProjection.v.ts"] = "CooperativeProjectionMain",
        ["CooperativeSgemm.v.ts"] = "CooperativeSgemmMain",
        ["PackMatrixHalf.v.ts"] = "PackMatrixHalfMain",
        ["LinearEpilogue.v.ts"] = "LinearEpilogueMain",
        ["RowReduction.v.ts"] = "ReductionMain",
        ["LayerNorm384"] = "LayerNorm768Main",
        ["LayerNorm768.v.ts"] = "LayerNorm768Main",
        ["FusedAttention64.Causal"] = "FusedAttention64Main",
        ["FusedAttention64.Packed"] = "FusedAttention64Main",
        ["FusedAttention64.v.ts"] = "FusedAttention64Main",
        ["Normalization.v.ts"] = "NormalizationMain",
        ["ProjectionEpilogue.v.ts"] = "ProjectionEpilogueMain",
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
        for (uint index = 0; index < DescriptorBindingCount; index++)
        {
            bindings[index] = new(index, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit);
        }
        var layout = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = DescriptorBindingCount, PBindings = bindings,
        };
        fixed (DescriptorSetLayout* value = &setLayout)
        {
            Check(plant.Vk.CreateDescriptorSetLayout(plant.Device, &layout, null, value));
        }
        var size = new DescriptorPoolSize(DescriptorType.StorageBuffer, (uint)MaximumDispatches * DescriptorBindingCount);
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
        var pushRange = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, (uint)sizeof(KernelParameters));
        var pipelineInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &descriptorLayout,
            PushConstantRangeCount = PushDispatchParameters ? 1u : 0u,
            PPushConstantRanges = PushDispatchParameters ? &pushRange : null,
        };
        fixed (PipelineLayout* value = &pipelineLayout)
        {
            Check(plant.Vk.CreatePipelineLayout(plant.Device, &pipelineInfo, null, value));
        }
    }

    private DispatchSlot CreateSlot(DescriptorPool? pool = null, bool deviceLocalParameters = false)
    {
        var descriptorLayout = setLayout;
        var allocation = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = pool ?? descriptorPool,
            DescriptorSetCount = 1, PSetLayouts = &descriptorLayout,
        };
        DescriptorSet set;
        Check(plant.Vk.AllocateDescriptorSets(plant.Device, &allocation, &set));
        return new(set, PushDispatchParameters ? null : Buffer((ulong)sizeof(KernelParameters),
            deviceLocalParameters ? VulkanBufferUsage.Storage | VulkanBufferUsage.TransferDestination : VulkanBufferUsage.Storage,
            deviceLocalParameters ? VulkanMemoryUsage.GpuOnly : VulkanMemoryUsage.CpuToGpu));
    }

    private void Configure(DispatchSlot slot, ComputeMathDispatch dispatch)
    {
        if (slot.Parameters is { } parameterBuffer)
        {
            KernelParameters parameters = dispatch.Parameters;
            var parameterBytes = MemoryMarshal.AsBytes(new ReadOnlySpan<KernelParameters>(in parameters));
            var previousBytes = MemoryMarshal.AsBytes(new ReadOnlySpan<KernelParameters>(in slot.LastParameters));
            if (!ReuseDispatchBindings || !slot.HasParameters || !parameterBytes.SequenceEqual(previousBytes))
            {
                if (parameterBuffer.MemoryUsage == VulkanMemoryUsage.GpuOnly)
                {
                    var upload = uploader.Upload(new(parameterBuffer, parameterBytes.ToArray(),
                        DebugName: "compute-math.prepared-parameters"));
                    Require(upload.Success, "Device-local kernel parameter upload failed: "
                        + string.Join("; ", upload.Diagnostics.Select(item => item.Message)));
                }
                else
                {
                    Require(parameterBuffer.Write(parameterBytes).Success, "Kernel parameter upload failed.");
                }
                slot.LastParameters = parameters;
                slot.HasParameters = true;
            }
        }
        var input = dispatch.Input.Buffer;
        var auxiliary = dispatch.Auxiliary.Buffer;
        var output = dispatch.Output.Buffer;
        // Buffer wrappers have distinct lifetimes even when Vulkan recycles a raw handle.
        if (ReuseDispatchBindings && ReferenceEquals(input, slot.Input)
            && ReferenceEquals(auxiliary, slot.Auxiliary) && ReferenceEquals(output, slot.Output))
        {
            return;
        }
        DescriptorBufferInfo* infos = stackalloc DescriptorBufferInfo[4];
        infos[0] = new(input.NativeBuffer, 0, input.SizeBytes);
        infos[1] = new(auxiliary.NativeBuffer, 0, auxiliary.SizeBytes);
        infos[2] = new(output.NativeBuffer, 0, output.SizeBytes);
        if (slot.Parameters is { } buffer)
        {
            infos[3] = new(buffer.NativeBuffer, 0, buffer.SizeBytes);
        }
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[4];
        for (int index = 0; index < DescriptorBindingCount; index++)
        {
            writes[index] = new()
            {
                SType = StructureType.WriteDescriptorSet, DstSet = slot.Set, DstBinding = (uint)index,
                DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &infos[index],
            };
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, DescriptorBindingCount, writes, 0, null);
        slot.Input = input;
        slot.Auxiliary = auxiliary;
        slot.Output = output;
    }

    private void RecordDispatch(CommandBuffer command, DispatchSlot slot, ComputeMathDispatch dispatch)
    {
        var set = slot.Set;
        plant.Vk.CmdBindPipeline(command, PipelineBindPoint.Compute, pipelines[dispatch.Shader]);
        plant.Vk.CmdBindDescriptorSets(command, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &set, 0, null);
        if (PushDispatchParameters)
        {
            var parameters = dispatch.Parameters;
            plant.Vk.CmdPushConstants(command, pipelineLayout, ShaderStageFlags.ComputeBit,
                0, (uint)sizeof(KernelParameters), &parameters);
        }
        plant.Vk.CmdDispatch(command, dispatch.GroupsX, dispatch.GroupsY, 1);
    }

    /// <summary>Optional profiling callback when a previously unseen kernel pipeline is created.</summary>
    public Action<string, double>? PipelineCreated { get; set; }

    private void EnsurePipeline(string shader)
    {
        if (pipelines.ContainsKey(shader))
        {
            return;
        }
        var observe = PipelineCreated;
        long start = observe is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
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
                    Flags = shader is "CooperativeSgemm.v.ts" or "CooperativeAttention64.v.ts" or "WideCooperativeAttention64.v.ts" or "WideCooperativeAttention64.Keys64" or "CooperativeProjection.v.ts" ? PipelineShaderStageCreateFlags.RequireFullSubgroupsBit : 0,
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
        observe?.Invoke(shader, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}
