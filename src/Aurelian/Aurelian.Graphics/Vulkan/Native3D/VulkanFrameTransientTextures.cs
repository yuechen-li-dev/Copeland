using Aurelian.Graphics.FramePlans;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Textures;

namespace Aurelian.Graphics.Vulkan.Native3D;

public sealed record FramePlanOptions(bool OptimizeInactivePasses = true, bool AliasTransientTextures = true,
    bool AsyncParticleUpdates = false);

/// <summary>Real image reuse for the diffuse scratch/fog lifetime, resolved by the same plan compiler.</summary>
internal sealed class VulkanFrameTransientTextures : IDisposable
{
    private readonly AurelianVulkanPlant plant;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly uint width;
    private readonly uint height;
    private readonly Dictionary<int, AurelianVulkanTexture> images = [];
    private readonly FramePlan placement;

    public VulkanFrameTransientTextures(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator,
        uint width, uint height, bool alias)
    {
        this.plant = plant;
        this.allocator = allocator;
        this.width = width;
        this.height = height;
        // Allocate against the production declaration with all optional consumers enabled.
        // A settings change can shorten these lifetimes, but cannot extend them beyond this profile.
        var complete = new Native3DFeatures(Surface: true, Modern: true, Motion: true, Shadows: true,
            Ambient: true, Tiles: true, Temporal: true, Bloom: true, Subsurface: true, Fog: true,
            Volume: true, Refraction: true, Transparency: true, Particles: false, AsyncParticles: false,
            GpuHistory: false, LocalShadows: 2, Alias: alias);
        placement = Native3DFrameGraph.Compile(complete, (int)width, (int)height);
    }

    public AurelianVulkanTexture Get(string name)
    {
        int id = placement.Graph.Resources.Single(resource => resource.Name == name).Id;
        int slot = placement.Allocations.Single(allocation => allocation.Resource == id).Slot;
        if (!images.TryGetValue(slot, out var image))
        {
            image = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, width, height,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource,
                VulkanMemoryUsage.GpuOnly, name, VulkanTextureFormat.Rgba16Float);
            images.Add(slot, image);
        }
        return image;
    }

    public int PhysicalImages => images.Count;
    public ulong AllocatedBytes => images.Values.Aggregate(0UL, (bytes, image) => bytes + image.ResourceState.SizeBytes);

    public void Dispose()
    {
        foreach (var image in images.Values)
        {
            image.Dispose();
        }
    }
}
