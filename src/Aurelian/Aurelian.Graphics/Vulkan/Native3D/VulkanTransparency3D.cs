using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Weighted blended color and logarithmic revealage, without sorting or depth writes.</summary>
internal sealed class VulkanTransparency3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly Stack<IDisposable> owned = new();
    private readonly AurelianVulkanRenderPass accumulationPass;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly VulkanPostProcess3D resolvePass;
    private readonly VulkanModel3DBatches models;
    private readonly AurelianVulkanTexture accumulation;
    private readonly AurelianVulkanTexture optical;
    private readonly AurelianVulkanTexture output;
    private NativeModel3DBatch[] batches = [];

    public VulkanTransparency3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, SurfaceLightingPrograms programs,
        uint width, uint height, AurelianVulkanTexture shadow, Sampler sampler)
    {
        this.plant = plant;
        try
        {
            accumulation = Image("transparency.color", VulkanTextureFormat.Rgba32Float);
            optical = Image("transparency.optical", VulkanTextureFormat.Rgba32Float);
            output = Image("transparency.resolved", VulkanTextureFormat.Rgba16Float);
            var attachments = new[] { accumulation, optical }.Select(texture =>
                new VulkanRenderPassAttachmentDescriptor("transparency.sum", texture.Format,
                    VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment)).ToArray();
            var pass = VulkanRenderPassFactory.Create(plant, new(attachments));
            if (!pass.Success)
                throw new InvalidOperationException(string.Join("; ", pass.Diagnostics.Select(item => item.Message)));
            accumulationPass = Own(pass.RenderPass!);
            var created = VulkanFramebufferFactory.Create(plant, accumulationPass, new(width, height, [accumulation, optical]));
            if (!created.Success)
                throw new InvalidOperationException(string.Join("; ", created.Diagnostics.Select(item => item.Message)));
            framebuffer = Own(created.Framebuffer!);
            models = Own(new VulkanModel3DBatches(plant, allocator, commands, fences, accumulationPass,
                programs.TransparentModel!, shadow, sampler, weightedTransparency: true));
            resolvePass = Own(new VulkanPostProcess3D(plant, allocator, programs.TransparencyResolve!, output, 3, Filter.Nearest));
        }
        catch
        {
            Dispose();
            throw;
        }

        AurelianVulkanTexture Image(string name, VulkanTextureFormat format)
        {
            return Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource,
                VulkanMemoryUsage.GpuOnly, name, format));
        }
    }

    public void Prepare(IReadOnlyList<NativeModel3DBatch> source, Matrix4x4 clip, Vector3 eye,
        float[] lighting, Matrix4x4 previousClip, float[] frame, AurelianVulkanTexture[] inputs)
    {
        batches = source.Where(batch => batch.Material.AlphaBlend).ToArray();
        models.ConfigureTransparency(frame, inputs);
        models.Upload(batches, clip, eye, lighting, previousClip, true);
    }

    public void Prepare(StaticModel model)
    {
        models.Prepare(model);
    }

    public AurelianVulkanTexture Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        AurelianVulkanTexture scene)
    {
        var encoder = new VulkanRenderPassCommandEncoder();
        var begin = encoder.Begin(plant, command, new(accumulationPass, framebuffer, new(0, 0, 0, 0))
        {
            AdditionalClearColors = [new(0, 0, 0, 0)],
        });
        if (!begin.Success)
            throw new InvalidOperationException("Weighted transparency accumulation begin failed.");
        models.Draw(command, begin.Scope!.Value, framebuffer, batches);
        if (!encoder.End(plant, command, begin.Scope.Value).Success)
            throw new InvalidOperationException("Weighted transparency accumulation end failed.");
        resolvePass.Configure([0, 0, 0, 0], scene, accumulation, optical);
        resolvePass.Record(command, triangle);
        return output;
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    public void Dispose()
    {
        while (owned.TryPop(out var resource))
            resource.Dispose();
    }
}
