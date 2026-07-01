using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;

public sealed unsafe class AurelianVulkanFramebuffer : IDisposable
{
    private readonly Vk vk;
    private Silk.NET.Vulkan.Device device;
    private bool disposed;

    internal AurelianVulkanFramebuffer(
        Vk vk,
        Silk.NET.Vulkan.Device device,
        Framebuffer framebuffer,
        PlantId plantId,
        uint width,
        uint height,
        VulkanFramebufferDescriptor descriptor,
        AurelianVulkanRenderPass renderPass)
    {
        this.vk = vk;
        this.device = device;
        NativeFramebuffer = framebuffer;
        PlantId = plantId;
        Width = width;
        Height = height;
        Descriptor = descriptor;
        RenderPass = renderPass;
    }

    public PlantId PlantId { get; }

    public uint Width { get; }

    public uint Height { get; }

    public VulkanFramebufferDescriptor Descriptor { get; }

    public AurelianVulkanRenderPass RenderPass { get; }

    public bool IsDisposed => disposed;

    internal Framebuffer NativeFramebuffer { get; private set; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (NativeFramebuffer.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyFramebuffer(device, NativeFramebuffer, (AllocationCallbacks*)null);
            NativeFramebuffer = default;
        }

        device = default;
    }
}
