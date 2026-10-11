using System.Numerics;
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
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>GPU billboards accumulate in the existing weighted transparency pass.</summary>
internal sealed unsafe class VulkanParticleBillboards3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly DescriptorSetLayout layout;
    private readonly DescriptorPool pool;
    private readonly DescriptorSet set;
    private readonly Sampler sampler;
    private readonly AurelianVulkanBuffer uniform;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private bool disposed;

    public VulkanParticleBillboards3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        AurelianVulkanRenderPass pass, CompiledGraphicsProgram program)
    {
        this.plant = plant;
        string[] fields = ["clipX", "clipY", "clipZ", "clipW", "inverseX", "inverseY", "inverseZ", "inverseW",
            "eye", "texels", "fogParameters", "fogColor", "volumeGrid", "volumeDepth"];
        if (program.Material is not { Size: 224, Set: 0, Binding: 0 } material
            || !material.Fields.Select(field => field.Name).SequenceEqual(fields)
            || !material.Fields.Select(field => field.Offset).SequenceEqual(Enumerable.Range(0, 14).Select(index => index * 16))
            || material.Fields.Any(field => field.PhysicalType != "float4")
            || program.Resources.Count != 5 || program.PixelTargets.Count != 2
            || !program.Resources.Select(resource => resource.Binding).Order().SequenceEqual([0, 1, 2, 3, 4])
            || program.Resources.Any(resource => resource.Set != 0)
            || !program.VertexInputs.Select(input => input.Location).SequenceEqual([0, 1, 2])
            || !program.VertexInputs.Select(input => input.PhysicalType).SequenceEqual(["float3", "float4", "float4"]))
        {
            throw new ArgumentException("AUR-VFX-007: Billboard shader does not match its uniform, vertex and OIT ABI.");
        }
        try
        {
            layout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            var descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(program.Shaders,
                [new(0, 44)], [new(0, 0, VulkanVertexAttributeFormat.Float3, 0),
                    new(1, 0, VulkanVertexAttributeFormat.Float4, 12), new(2, 0, VulkanVertexAttributeFormat.Float4, 28)]);
            Require(descriptor.Success, "Particle vertex descriptor failed.");
            var result = VulkanGraphicsPipelineFactory.Create(plant, pass,
                descriptor.Descriptor! with { EnableAdditiveBlend = true }, [layout]);
            Require(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
            pipeline = result.Pipeline!;
            uniform = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 224,
                VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "particles3d.draw");
            (pool, set) = Vulkan3DPass.AllocateSet(plant, layout, uniform, 2);
            sampler = Vulkan3DPass.CreateSampler(plant, Filter.Nearest);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Configure(Matrix4x4 clip, Vector3 eye, Graphics3DSettings settings, uint width, uint height,
        AurelianVulkanTexture motion, AurelianVulkanTexture volume, float[] volumeParameters)
    {
        if (!Matrix4x4.Invert(clip, out var inverse))
        {
            throw new ArgumentException("AUR-VFX-004: Particle depth reconstruction needs an invertible camera.");
        }
        var fog = settings.Fog;
        float[] data = [.. Lighting3DUniforms.Rows(clip), .. Lighting3DUniforms.Rows(inverse),
            eye.X, eye.Y, eye.Z, 0, 1f / width, 1f / height, 0, 0,
            fog.Density, fog.HeightFalloff, fog.BaseHeight, fog.StartDistance,
            fog.Color.X, fog.Color.Y, fog.Color.Z, fog.MaximumDistance, .. volumeParameters];
        Require(uniform.Write(MemoryMarshal.AsBytes(data.AsSpan())).Success, "Particle draw uniform upload failed.");
        Vulkan3DPass.WriteImage(plant, set, 1, motion, sampler);
        Vulkan3DPass.WriteImage(plant, set, 3, volume, sampler);
    }

    public void Draw(VulkanCommandBufferLease command, VulkanRenderPassScope scope,
        AurelianVulkanFramebuffer framebuffer, VulkanParticles3D particles)
    {
        if (particles.VertexCount == 0) { return; }
        DescriptorSet descriptor = set;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &descriptor, 0, null);
        var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, scope,
            new(pipeline, particles.Vertices, particles.VertexCount, 0, VulkanViewportScissor.FromFramebuffer(framebuffer)));
        Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
    }

    private static void Require(bool success, string message)
    {
        if (!success) { throw new InvalidOperationException(message); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (pool.Handle != 0) { plant.Vk.DestroyDescriptorPool(plant.Device, pool, null); }
        if (sampler.Handle != 0) { plant.Vk.DestroySampler(plant.Device, sampler, null); }
        pipeline?.Dispose();
        uniform?.Dispose();
        if (layout.Handle != 0) { plant.Vk.DestroyDescriptorSetLayout(plant.Device, layout, null); }
    }
}
