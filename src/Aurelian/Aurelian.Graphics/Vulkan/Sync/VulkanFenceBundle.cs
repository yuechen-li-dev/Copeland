using Aurelian.Graphics.Vulkan.Device;

namespace Aurelian.Graphics.Vulkan.Sync;

public sealed class VulkanFenceBundle : IDisposable
{
    private bool disposed;

    private VulkanFenceBundle(
        VulkanTimelineFence frameFence,
        VulkanTimelineFence commandListFence,
        VulkanTimelineFence copyFence)
    {
        FrameFence = frameFence;
        CommandListFence = commandListFence;
        CopyFence = copyFence;
    }

    public VulkanTimelineFence FrameFence { get; }

    public VulkanTimelineFence CommandListFence { get; }

    public VulkanTimelineFence CopyFence { get; }

    public static VulkanFenceBundle Create(AurelianVulkanPlant plant)
    {
        ArgumentNullException.ThrowIfNull(plant);

        VulkanTimelineFence? frameFence = null;
        VulkanTimelineFence? commandListFence = null;
        VulkanTimelineFence? copyFence = null;

        try
        {
            string prefix = $"plant-{plant.Context.Id}";
            frameFence = VulkanTimelineFence.Create(plant, $"{prefix}-frame-fence");
            commandListFence = VulkanTimelineFence.Create(plant, $"{prefix}-command-list-fence");
            copyFence = VulkanTimelineFence.Create(plant, $"{prefix}-copy-fence");
            return new VulkanFenceBundle(frameFence, commandListFence, copyFence);
        }
        catch
        {
            copyFence?.Dispose();
            commandListFence?.Dispose();
            frameFence?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CopyFence.Dispose();
        CommandListFence.Dispose();
        FrameFence.Dispose();
    }
}
