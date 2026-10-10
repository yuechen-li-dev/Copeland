using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Resources for the bounded shadow and HDR output passes. Submission remains owned by the scene renderer.</summary>
internal sealed unsafe class Vulkan3DPass : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly Stack<IDisposable> owned = new();
    private readonly DescriptorSetLayout layout;
    private readonly DescriptorPool pool;
    private readonly DescriptorSet set;
    private readonly Sampler sampler;
    private readonly AurelianVulkanBuffer uniform;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly AurelianVulkanGraphicsPipeline? modelPipeline;
    private bool disposed;

    public Vulkan3DPass(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator, CompiledGraphicsProgram program,
        AurelianVulkanTexture color, uint width, uint height, bool shadow, AurelianVulkanTexture? sampled = null)
    {
        this.plant = plant;
        Validate(program, shadow, sampled is not null);
        try
        {
            var pass = VulkanRenderPassFactory.Create(plant, new(
                [new("3d.pass.color", color.Format, VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined, shadow ? VulkanResourceLayout.ShaderResourceFragment : VulkanResourceLayout.TransferSource)],
                shadow ? new("3d.pass.depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.DontCare, VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment) : null));
            Require(pass.Success, string.Join("; ", pass.Diagnostics.Select(item => item.Message)));
            Pass = Own(pass.RenderPass!);
            AurelianVulkanTexture? depth = null;
            if (shadow)
            {
                depth = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                    VulkanTextureUsage.DepthAttachment, VulkanMemoryUsage.GpuOnly, "shadow.depth", VulkanTextureFormat.D32Float));
            }
            var framebuffer = VulkanFramebufferFactory.Create(plant, Pass, new(width, height, [color], depth));
            Require(framebuffer.Success, string.Join("; ", framebuffer.Diagnostics.Select(item => item.Message)));
            Framebuffer = Own(framebuffer.Framebuffer!);
            layout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            uint stride = shadow ? 40u : 8u;
            pipeline = CreatePipeline(program, stride, shadow);
            if (shadow)
            {
                modelPipeline = CreatePipeline(program, 64, true);
            }
            int bytes = program.Material?.Size ?? throw new ArgumentException("3D pass requires a material uniform.", nameof(program));
            uniform = Own(VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, (ulong)bytes,
                VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "3d.pass.uniform"));
            (pool, set) = AllocateSet(plant, layout, uniform, sampled is null ? 0u : (uint)((program.Resources.Count - 1) / 2));
            if (sampled is not null)
            {
                sampler = CreateSampler(plant, Filter.Linear);
                WriteImage(plant, set, 1, sampled, sampler);
                if (program.Resources.Count == 5) WriteImage(plant, set, 3, sampled, sampler);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AurelianVulkanRenderPass Pass { get; }
    public AurelianVulkanFramebuffer Framebuffer { get; }

    private static void Validate(CompiledGraphicsProgram program, bool shadow, bool sampled)
    {
        string[] fields = shadow ? ["clipX", "clipY", "clipZ", "clipW"] : ["parameters"];
        var material = program.Material;
        bool valid = material is not null && material.Size == fields.Length * 16 && material.Set == 0 && material.Binding == 0
            && material.Fields.Select(item => item.Name).SequenceEqual(fields)
            && material.Fields.All(item => item.PhysicalType == "float4")
            && material.Fields.Select(item => item.Offset).SequenceEqual(Enumerable.Range(0, fields.Length).Select(index => index * 16))
            && program.VertexInputs.Count == 1 && program.VertexInputs[0].Location == 0
            && program.VertexInputs[0].PhysicalType == (shadow ? "float3" : "float2")
            && (shadow ? program.Resources.Count == 1 : program.Resources.Count is 3 or 5) && sampled == !shadow;
        foreach (var resource in program.Resources)
        {
            CompiledGraphicsResourceKind expected = resource.Binding switch
            {
                0 => CompiledGraphicsResourceKind.UniformBuffer,
                1 or 3 => CompiledGraphicsResourceKind.Texture2D,
                _ => CompiledGraphicsResourceKind.Sampler,
            };
            valid &= resource.Set == 0 && resource.Binding >= 0 && resource.Binding < program.Resources.Count && resource.Kind == expected
                && resource.Visibility.SequenceEqual(shadow ? [CompiledGraphicsStage.Vertex] : [CompiledGraphicsStage.Fragment]);
        }
        if (!valid)
        {
            throw new ArgumentException("3D pass shader does not match its typed shadow/output resource contract.", nameof(program));
        }
    }

    public void SetOutputTextures(AurelianVulkanTexture source, AurelianVulkanTexture bloom)
    {
        WriteImage(plant, set, 1, source, sampler);
        WriteImage(plant, set, 3, bloom, sampler);
    }

    public void Upload(ReadOnlySpan<float> values)
    {
        Require(uniform.Write(MemoryMarshal.AsBytes(values)).Success, "3D pass uniform upload failed.");
    }

    public void Draw(VulkanCommandBufferLease command, VulkanRenderPassScope scope, AurelianVulkanBuffer vertices,
        uint count, bool model = false, uint first = 0)
    {
        DescriptorSet descriptor = set;
        AurelianVulkanGraphicsPipeline selected = model ? modelPipeline! : pipeline;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            selected.NativePipelineLayout, 0, 1, &descriptor, 0, null);
        var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, scope,
            new(selected, vertices, count, first, VulkanViewportScissor.FromFramebuffer(Framebuffer)));
        Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
    }

    private AurelianVulkanGraphicsPipeline CreatePipeline(CompiledGraphicsProgram program, uint stride, bool shadow)
    {
        var descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(program.Shaders,
            [new(0, stride)], [new(0, 0, shadow ? VulkanVertexAttributeFormat.Float3 : VulkanVertexAttributeFormat.Float2, 0)],
            enableDepthTest: shadow, enableDepthWrite: shadow);
        Require(descriptor.Success, string.Join("; ", descriptor.Diagnostics.Select(item => item.Message)));
        var result = VulkanGraphicsPipelineFactory.Create(plant, Pass, descriptor.Descriptor!, [layout]);
        Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return Own(result.Pipeline!);
    }

    internal static (DescriptorPool Pool, DescriptorSet Set) AllocateSet(AurelianVulkanPlant plant,
        DescriptorSetLayout layout, AurelianVulkanBuffer uniform, uint imageCount)
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3]
        {
            new(DescriptorType.UniformBuffer, 1),
            new(DescriptorType.SampledImage, imageCount),
            new(DescriptorType.Sampler, imageCount),
        };
        DescriptorPoolCreateInfo info = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = imageCount == 0 ? 1u : 3u,
            PPoolSizes = sizes,
        };
        Require(plant.Vk.CreateDescriptorPool(plant.Device, &info, null, out DescriptorPool pool) == Result.Success, "3D descriptor pool failed.");
        try
        {
            DescriptorSetAllocateInfo allocate = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            Require(plant.Vk.AllocateDescriptorSets(plant.Device, &allocate, out DescriptorSet set) == Result.Success, "3D descriptor allocation failed.");
            DescriptorBufferInfo buffer = new(uniform.NativeBuffer, 0, uniform.SizeBytes);
            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &buffer,
            };
            plant.Vk.UpdateDescriptorSets(plant.Device, 1, &write, 0, null);
            return (pool, set);
        }
        catch
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
            throw;
        }
    }

    internal static Sampler CreateSampler(AurelianVulkanPlant plant, Filter filter)
    {
        SamplerCreateInfo info = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = filter,
            MinFilter = filter,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = 0,
        };
        Require(plant.Vk.CreateSampler(plant.Device, &info, null, out Sampler sampler) == Result.Success, "3D sampler creation failed.");
        return sampler;
    }

    internal static void WriteImage(AurelianVulkanPlant plant, DescriptorSet set, uint binding, AurelianVulkanTexture texture, Sampler sampler)
    {
        DescriptorImageInfo image = new(default, texture.NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal);
        DescriptorImageInfo filter = new(sampler, default, ImageLayout.Undefined);
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[2];
        writes[0] = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = 1,
            PImageInfo = &image,
        };
        writes[1] = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding + 1,
            DescriptorType = DescriptorType.Sampler,
            DescriptorCount = 1,
            PImageInfo = &filter,
        };
        plant.Vk.UpdateDescriptorSets(plant.Device, 2, writes, 0, null);
    }

    internal static void SampleAfterRendering(AurelianVulkanPlant plant, VulkanCommandBufferLease command, AurelianVulkanTexture texture)
    {
        var transition = texture.LayoutTracker.Transition("3d.pass.sample", 0, 0, VulkanResourceLayout.ShaderResourceFragment);
        Require(transition.Success, "3D sample transition rejected.");
        if (transition.Plan is not null)
        {
            var barrier = VulkanBarrierCommandEmitter.EmitTextureBarriers(plant, command, [new(texture, transition.Plan)]);
            Require(barrier.Success, string.Join("; ", barrier.Diagnostics.Select(item => item.Message)));
        }
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (pool.Handle != 0)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        }
        if (sampler.Handle != 0)
        {
            plant.Vk.DestroySampler(plant.Device, sampler, null);
        }
        while (owned.TryPop(out IDisposable? resource))
        {
            resource.Dispose();
        }
        if (layout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, layout, null);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
