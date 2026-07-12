using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Resources.Textures;

public sealed unsafe class AurelianVulkanTexture : IDisposable
{
    private readonly Vk vk;
    private Silk.NET.Vulkan.Device device;
    private VulkanMemoryAllocation? allocation;
    private bool disposed;

    internal AurelianVulkanTexture(
        Vk vk,
        Silk.NET.Vulkan.Device device,
        Image image,
        ImageView? imageView,
        VulkanMemoryAllocation allocation,
        PlantId plantId,
        uint width,
        uint height,
        uint mipLevels,
        uint arrayLayers,
        VulkanTextureFormat format,
        VulkanTextureUsage usage,
        VulkanResourceLayout initialLayout)
    {
        this.vk = vk;
        this.device = device;
        this.allocation = allocation;
        NativeImage = image;
        NativeImageView = imageView;
        PlantId = plantId;
        Width = width;
        Height = height;
        MipLevels = mipLevels;
        ArrayLayers = arrayLayers;
        Format = format;
        Usage = usage;
        InitialLayout = initialLayout;
        ResourceState = new GpuResourceState(plantId, allocation.SizeBytes, allocation.Backend);
        LayoutTracker = new VulkanLayoutTracker(mipLevels, arrayLayers, initialLayout);
    }

    public PlantId PlantId { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public VulkanTextureFormat Format { get; }

    public VulkanTextureUsage Usage { get; }

    public VulkanResourceLayout InitialLayout { get; }

    public GpuResourceState ResourceState { get; }

    public VulkanLayoutTracker LayoutTracker { get; }

    public bool IsDisposed => disposed;

    internal Image NativeImage { get; private set; }

    internal ImageView? NativeImageView { get; private set; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (NativeImageView is { Handle: not 0 } imageView && device.Handle != 0)
        {
            vk.DestroyImageView(device, imageView, (AllocationCallbacks*)null);
            NativeImageView = null;
        }

        if (NativeImage.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyImage(device, NativeImage, (AllocationCallbacks*)null);
            NativeImage = default;
        }

        allocation?.Dispose();
        allocation = null;
        device = default;
    }
}
