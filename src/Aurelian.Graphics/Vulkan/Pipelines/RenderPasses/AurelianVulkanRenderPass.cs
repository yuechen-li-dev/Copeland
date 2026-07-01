using Aurelian.Graphics.Plants;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

public sealed unsafe class AurelianVulkanRenderPass : IDisposable
{
    private readonly Vk vk;
    private Silk.NET.Vulkan.Device device;
    private bool disposed;

    internal AurelianVulkanRenderPass(
        Vk vk,
        Silk.NET.Vulkan.Device device,
        RenderPass renderPass,
        PlantId plantId,
        VulkanRenderPassDescriptor descriptor)
    {
        this.vk = vk;
        this.device = device;
        NativeRenderPass = renderPass;
        PlantId = plantId;
        Descriptor = descriptor;
    }

    public PlantId PlantId { get; }

    public VulkanRenderPassDescriptor Descriptor { get; }

    public bool IsDisposed => disposed;

    internal RenderPass NativeRenderPass { get; private set; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (NativeRenderPass.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyRenderPass(device, NativeRenderPass, (AllocationCallbacks*)null);
            NativeRenderPass = default;
        }

        device = default;
    }
}
