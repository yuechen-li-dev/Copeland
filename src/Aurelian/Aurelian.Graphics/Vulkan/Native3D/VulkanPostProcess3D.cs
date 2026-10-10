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

/// <summary>A typed fullscreen pass. Its output and inputs remain owned by the scene renderer.</summary>
internal sealed unsafe class VulkanPostProcess3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly Stack<IDisposable> owned = new();
    private readonly DescriptorSetLayout layout;
    private readonly DescriptorPool pool;
    private readonly DescriptorSet set;
    private readonly Sampler sampler;
    private readonly Sampler linearSampler;
    private readonly AurelianVulkanBuffer uniform;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly int inputCount;
    private bool disposed;

    public VulkanPostProcess3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        CompiledGraphicsProgram program, AurelianVulkanTexture output, int inputCount, Filter filter = Filter.Linear)
    {
        this.plant = plant;
        this.inputCount = inputCount;
        Validate(program, inputCount);
        try
        {
            var pass = VulkanRenderPassFactory.Create(plant, new([
                new("post.color", output.Format, VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment)]));
            Require(pass.Success, string.Join("; ", pass.Diagnostics.Select(item => item.Message)));
            Pass = Own(pass.RenderPass!);
            var framebuffer = VulkanFramebufferFactory.Create(plant, Pass, new(output.Width, output.Height, [output]));
            Require(framebuffer.Success, string.Join("; ", framebuffer.Diagnostics.Select(item => item.Message)));
            Framebuffer = Own(framebuffer.Framebuffer!);
            layout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            var descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(program.Shaders,
                [new(0, 8)], [new(0, 0, VulkanVertexAttributeFormat.Float2, 0)]);
            Require(descriptor.Success, "Fullscreen pipeline descriptor rejected.");
            var created = VulkanGraphicsPipelineFactory.Create(plant, Pass, descriptor.Descriptor!, [layout]);
            Require(created.Success, string.Join("; ", created.Diagnostics.Select(item => item.Message)));
            pipeline = Own(created.Pipeline!);
            uniform = Own(VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator,
                (ulong)program.Material!.Size, VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "post.uniform"));
            (pool, set) = Vulkan3DPass.AllocateSet(plant, layout, uniform, (uint)inputCount);
            sampler = Vulkan3DPass.CreateSampler(plant, filter);
            linearSampler = Vulkan3DPass.CreateSampler(plant, Filter.Linear);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AurelianVulkanRenderPass Pass { get; }
    public AurelianVulkanFramebuffer Framebuffer { get; }

    public void Configure(float[] values, params AurelianVulkanTexture[] inputs)
    {
        if (inputs.Length != inputCount || values.Length * 4 != (int)uniform.SizeBytes)
            throw new ArgumentException("Fullscreen resources and uniform must match the compiled shader ABI.");
        Require(uniform.Write(MemoryMarshal.AsBytes(values.AsSpan())).Success, "Fullscreen uniform upload failed.");
        for (int index = 0; index < inputs.Length; index++)
        {
            if (inputs[index] == Framebuffer.Descriptor.ColorAttachments[0])
                throw new InvalidOperationException("A fullscreen pass cannot sample its own render target.");
            Vulkan3DPass.WriteImage(plant, set, (uint)(1 + index * 2), inputs[index], sampler);
        }
    }

    public void Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle)
    {
        var encoder = new VulkanRenderPassCommandEncoder();
        var begin = encoder.Begin(plant, command, new(Pass, Framebuffer, new(0, 0, 0, 1)));
        Require(begin.Success, "Fullscreen pass begin failed.");
        DescriptorSet descriptor = set;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &descriptor, 0, null);
        var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
            new(pipeline, triangle, 3, 0, VulkanViewportScissor.FromFramebuffer(Framebuffer)));
        Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        Require(encoder.End(plant, command, begin.Scope.Value).Success, "Fullscreen pass end failed.");
    }

    public void SetLinearInput(int index, AurelianVulkanTexture texture)
    {
        if (index < 0 || index >= inputCount || texture == Framebuffer.Descriptor.ColorAttachments[0])
        {
            throw new ArgumentException("Linear fullscreen input must be an admitted input distinct from the output.");
        }
        Vulkan3DPass.WriteImage(plant, set, (uint)(1 + index * 2), texture, linearSampler);
    }

    private static void Validate(CompiledGraphicsProgram program, int count)
    {
        bool valid = count is >= 1 and <= 12 && program.Material is { Set: 0, Binding: 0 } material
            && material.Size == material.Fields.Count * 16
            && material.Fields.All(field => field.PhysicalType == "float4")
            && material.Fields.Select(field => field.Offset).SequenceEqual(Enumerable.Range(0, material.Fields.Count).Select(index => index * 16))
            && program.VertexInputs.Count == 1 && program.VertexInputs[0].Location == 0
            && program.VertexInputs[0].PhysicalType == "float2" && program.Resources.Count == 1 + count * 2;
        foreach (var resource in program.Resources)
        {
            var kind = CompiledGraphicsResourceKind.Sampler;
            if (resource.Binding == 0) kind = CompiledGraphicsResourceKind.UniformBuffer;
            else if (resource.Binding % 2 == 1) kind = CompiledGraphicsResourceKind.Texture2D;
            valid &= resource.Set == 0 && resource.Kind == kind
                && resource.Visibility.SequenceEqual([CompiledGraphicsStage.Fragment]);
        }
        valid &= program.Resources.Select(item => item.Binding).Order().SequenceEqual(Enumerable.Range(0, 1 + count * 2));
        if (!valid) throw new ArgumentException("Fullscreen shader must expose packed float4 uniforms and numbered image/sampler pairs.");
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (pool.Handle != 0) plant.Vk.DestroyDescriptorPool(plant.Device, pool, null);
        if (sampler.Handle != 0) plant.Vk.DestroySampler(plant.Device, sampler, null);
        if (linearSampler.Handle != 0) plant.Vk.DestroySampler(plant.Device, linearSampler, null);
        while (owned.TryPop(out var resource)) resource.Dispose();
        if (layout.Handle != 0) plant.Vk.DestroyDescriptorSetLayout(plant.Device, layout, null);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
