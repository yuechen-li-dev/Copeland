using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Five-level HDR tent pyramid. Allocations persist; no readback or per-frame shader compilation.</summary>
internal sealed class VulkanBloom3D : IDisposable
{
    private readonly List<AurelianVulkanTexture> down = [];
    private readonly List<AurelianVulkanTexture> up = [];
    private readonly List<VulkanPostProcess3D> downPasses = [];
    private readonly List<VulkanPostProcess3D> upPasses = [];

    public VulkanBloom3D(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        CompiledGraphicsProgram program, uint width, uint height)
    {
        try
        {
            for (int level = 0; level < 5; level++)
            {
                width = Math.Max(width / 2, 1);
                height = Math.Max(height / 2, 1);
                var image = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource, VulkanMemoryUsage.GpuOnly,
                    "bloom.down", VulkanTextureFormat.Rgba16Float);
                down.Add(image);
                downPasses.Add(new(plant, allocator, program, image, 2));
                if (width == 1 && height == 1) break;
            }
            for (int level = 0; level < down.Count - 1; level++)
            {
                var image = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, down[level].Width, down[level].Height,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource, VulkanMemoryUsage.GpuOnly,
                    "bloom.up", VulkanTextureFormat.Rgba16Float);
                up.Add(image);
                upPasses.Add(new(plant, allocator, program, image, 2));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AurelianVulkanTexture Record(VulkanCommandBufferLease command, AurelianVulkanBuffer triangle,
        AurelianVulkanTexture source, Graphics3DSettings settings)
    {
        AurelianVulkanTexture current = source;
        for (int level = 0; level < down.Count; level++)
        {
            downPasses[level].Configure([1f / current.Width, 1f / current.Height, settings.BloomThreshold,
                settings.BloomKnee, level == 0 ? 1 : 0, 0, 0, 0], current, current);
            downPasses[level].Record(command, triangle);
            current = down[level];
        }
        for (int level = up.Count - 1; level >= 0; level--)
        {
            upPasses[level].Configure([1f / down[level].Width, 1f / down[level].Height, 0, 0,
                0, .5f, 0, 0], down[level], current);
            upPasses[level].Record(command, triangle);
            current = up[level];
        }
        return current;
    }

    public void Dispose()
    {
        foreach (var pass in upPasses) pass.Dispose();
        foreach (var pass in downPasses) pass.Dispose();
        foreach (var image in up) image.Dispose();
        foreach (var image in down) image.Dispose();
    }
}
