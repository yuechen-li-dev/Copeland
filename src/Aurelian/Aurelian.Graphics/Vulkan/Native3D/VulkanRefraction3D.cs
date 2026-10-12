using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Nearest refractive surface per pixel. Background transport contains opaque geometry only.</summary>
internal sealed class VulkanRefraction3D : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly Stack<IDisposable> owned = new();
    private readonly AurelianVulkanRenderPass pass;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly VulkanModel3DBatches models;
    private readonly VulkanPostProcess3D resolve;
    private readonly AurelianVulkanTexture radiance;
    private readonly AurelianVulkanTexture coverage;
    private readonly AurelianVulkanTexture output;
    private readonly AurelianVulkanTexture motion;
    private readonly VulkanTemporal3D? waterTemporal;
    private NativeModel3DBatch[] batches = [];

    public VulkanRefraction3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, SurfaceLightingPrograms programs,
        uint width, uint height, AurelianVulkanTexture shadow, Sampler sampler)
    {
        this.plant = plant;
        try
        {
            radiance = Image("refraction.radiance", VulkanTextureFormat.Rgba16Float);
            coverage = Image("refraction.coverage", VulkanTextureFormat.Rgba8Unorm);
            output = Image("refraction.output", VulkanTextureFormat.Rgba16Float);
            motion = Image("refraction.motion", VulkanTextureFormat.Rgba32Float);
            var attachments = new[] { radiance, coverage, motion }.Select(texture =>
                new VulkanRenderPassAttachmentDescriptor("refraction.surface", texture.Format,
                    VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment)).ToArray();
            var createdPass = VulkanRenderPassFactory.Create(plant, new(attachments,
                new("refraction.depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.DontCare, VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment)));
            Require(createdPass.Success, "Refractive render pass creation failed.");
            pass = Own(createdPass.RenderPass!);
            AurelianVulkanTexture depth = Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator,
                width, height, VulkanTextureUsage.DepthAttachment, VulkanMemoryUsage.GpuOnly,
                "refraction.depth", VulkanTextureFormat.D32Float));
            var createdFramebuffer = VulkanFramebufferFactory.Create(plant, pass, new(width, height, [radiance, coverage, motion], depth));
            Require(createdFramebuffer.Success, "Refractive framebuffer creation failed.");
            framebuffer = Own(createdFramebuffer.Framebuffer!);
            models = Own(new VulkanModel3DBatches(plant, allocator, commands, fences, pass,
                programs.RefractiveModel!, shadow, sampler, refraction: true));
            resolve = Own(new VulkanPostProcess3D(plant, allocator, programs.RefractionResolve!, output, 3, Filter.Nearest));
            if (programs.WaterTemporal is not null)
            {
                waterTemporal = Own(new VulkanTemporal3D(plant, allocator, commands, fences, programs.WaterTemporal, width, height));
            }
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

    public void Prepare(StaticModel model)
    {
        models.Prepare(model);
    }

    public AurelianVulkanTexture Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        IReadOnlyList<NativeModel3DBatch> source, Matrix4x4 clip, Vector3 eye, float[] lighting,
        float[] frame, AurelianVulkanTexture[] inputs, AurelianVulkanTexture unfogged, AurelianVulkanTexture background,
        WaterFrame3D waterFrame, float previousTime, Matrix4x4 previousClip, Matrix4x4 logicalClip,
        bool resetHistory, bool temporal, float historyWeight,
        AurelianVulkanTexture? reflection, Matrix4x4 reflectionClip)
    {
        Matrix4x4.Invert(clip, out Matrix4x4 inverse);
        batches = source.Where(batch => batch.Material.Transmission > 0).ToArray();
        models.ConfigureTransparency([.. frame, .. Lighting3DUniforms.Rows(inverse),
            .. Water3DUniforms.Frame(waterFrame, previousTime, previousClip, reflectionClip, reflection is not null)],
            [.. inputs, unfogged, reflection ?? unfogged]);
        models.Upload(batches, clip, eye, lighting, clip, true);
        var encoder = new VulkanRenderPassCommandEncoder();
        var begin = encoder.Begin(plant, command, new(pass, framebuffer, new(0, 0, 0, 0))
        {
            AdditionalClearColors = [new(0, 0, 0, 0), new(0, 0, 0, 1)],
        });
        Require(begin.Success, "Refractive surface pass begin failed.");
        models.Draw(command, begin.Scope!.Value, framebuffer, batches);
        Require(encoder.End(plant, command, begin.Scope.Value).Success, "Refractive surface pass end failed.");
        AurelianVulkanTexture color = radiance;
        if (batches.Any(batch => batch.Material.Water is not null) && temporal)
        {
            if (waterTemporal is null)
            {
                throw new NotSupportedException("AUR-WATER-006: Temporal water requires the shared temporal shader.");
            }
            if (resetHistory)
            {
                waterTemporal.Reset();
            }
            waterTemporal.Begin(logicalClip);
            color = waterTemporal.Resolve(command, triangle, radiance, motion, historyWeight);
        }
        else
        {
            waterTemporal?.Reset();
        }
        resolve.Configure([0, 0, 0, 0], background, color, coverage);
        resolve.Record(command, triangle);
        return output;
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    private static void Require(bool success, string message)
    {
        if (!success)
            throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        while (owned.TryPop(out IDisposable? resource))
            resource.Dispose();
    }
}
