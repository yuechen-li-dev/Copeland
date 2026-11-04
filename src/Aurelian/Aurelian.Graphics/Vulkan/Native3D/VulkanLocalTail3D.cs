using System.Numerics;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Models;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Reconstructs the stochastic local-light signal before composing it with the current exact head.</summary>
internal sealed class VulkanLocalTail3D : IDisposable
{
    private readonly Stack<IDisposable> owned = new();
    private readonly AurelianVulkanTexture[] history = new AurelianVulkanTexture[2];
    private readonly AurelianVulkanTexture[] geometry = new AurelianVulkanTexture[2];
    private readonly AurelianVulkanTexture[] surfaces = new AurelianVulkanTexture[2];
    private readonly VulkanPostProcess3D[] temporal = new VulkanPostProcess3D[2];
    private readonly VulkanPostProcess3D[] spatial = new VulkanPostProcess3D[3];
    private readonly AurelianVulkanTexture[] filtered = new AurelianVulkanTexture[3];
    private readonly VulkanPostProcess3D compose;
    private Matrix4x4 previousInverse = Matrix4x4.Identity;
    private int writeIndex;
    private bool valid;
    public AurelianVulkanTexture Output { get; }

    public VulkanLocalTail3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        VulkanCommandBufferPool commands, VulkanFenceBundle fences, VulkanFrameInFlightSet frames,
        SurfaceLightingPrograms programs, uint width, uint height)
    {
        if (programs.TailTemporal is null || programs.TailSpatial is null || programs.TailCompose is null)
        {
            throw new NotSupportedException("AUR-TAIL-001: Local tail reconstruction programs were not configured.");
        }
        try
        {
            using var uploader = new VulkanTextureUploader(plant, allocator, commands, fences);
            for (int index = 0; index < 2; index++)
            {
                history[index] = Image("tail.history", VulkanTextureFormat.Rgba32Float);
                geometry[index] = Image("tail.geometry", VulkanTextureFormat.Rgba32Float);
                surfaces[index] = Image("tail.surface", VulkanTextureFormat.Rgba32Float);
                foreach (var texture in new[] { history[index], geometry[index], surfaces[index] })
                {
                    var uploaded = uploader.Upload(new(texture, new byte[checked((int)(width * height * 16))], "tail.initialize"));
                    if (!uploaded.Success) throw new InvalidOperationException("Local tail history initialization failed.");
                }
                temporal[index] = Own(new VulkanPostProcess3D(plant, allocator, programs.TailTemporal, history[index],
                    7, Filter.Nearest, geometry[index], frames, surfaces[index]));
            }
            for (int index = 0; index < 3; index++)
            {
                filtered[index] = Image("tail.filtered", VulkanTextureFormat.Rgba16Float);
                spatial[index] = Own(new VulkanPostProcess3D(plant, allocator, programs.TailSpatial, filtered[index], 4,
                    Filter.Nearest, frames: frames));
            }
            Output = Image("tail.composite", VulkanTextureFormat.Rgba16Float);
            compose = Own(new VulkanPostProcess3D(plant, allocator, programs.TailCompose, Output, 2, Filter.Nearest, frames: frames));
        }
        catch
        {
            Dispose();
            throw;
        }

        AurelianVulkanTexture Image(string name, VulkanTextureFormat format)
        {
            return Own(VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination
                | VulkanTextureUsage.TransferSource, VulkanMemoryUsage.GpuOnly, name, format));
        }
    }

    public void Reset() => valid = false;

    public AurelianVulkanTexture Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        AurelianVulkanTexture head, AurelianVulkanTexture rawTail, AurelianVulkanTexture normal,
        AurelianVulkanTexture motion, AurelianVulkanTexture surface, Matrix4x4 inverse, LocalLightSampling3D options)
    {
        float[] uniforms = [.. Lighting3DUniforms.Rows(inverse), .. Lighting3DUniforms.Rows(previousInverse),
            1f / head.Width, 1f / head.Height, valid ? 1 : 0, options.TailHistoryLimit, .02f, 1, 0, 0];
        temporal[writeIndex].Configure(uniforms, rawTail, normal, motion, surface, history[1 - writeIndex],
            geometry[1 - writeIndex], surfaces[1 - writeIndex]);
        temporal[writeIndex].Record(command, triangle);
        AurelianVulkanTexture result = history[writeIndex];
        for (int index = 0; index < options.TailSpatialPasses; index++)
        {
            uniforms[37] = 1 << index;
            spatial[index].Configure(uniforms, result, normal, motion, surface);
            spatial[index].Record(command, triangle);
            result = filtered[index];
        }
        compose.Configure([0, 0, 0, 0], head, result);
        compose.Record(command, triangle);
        previousInverse = inverse;
        writeIndex = 1 - writeIndex;
        valid = true;
        return Output;
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    public void Dispose()
    {
        while (owned.TryPop(out var item))
        {
            item.Dispose();
        }
    }
}
