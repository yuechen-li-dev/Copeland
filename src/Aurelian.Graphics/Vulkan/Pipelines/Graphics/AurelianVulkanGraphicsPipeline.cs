using Aurelian.Graphics.Plants;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed unsafe class AurelianVulkanGraphicsPipeline : IDisposable
{
    private readonly Vk vk;
    private Silk.NET.Vulkan.Device device;
    private bool disposed;

    internal AurelianVulkanGraphicsPipeline(
        Vk vk,
        Silk.NET.Vulkan.Device device,
        Pipeline pipeline,
        PipelineLayout pipelineLayout,
        PlantId plantId,
        VulkanGraphicsPipelineDescriptor descriptor)
    {
        this.vk = vk;
        this.device = device;
        NativePipeline = pipeline;
        NativePipelineLayout = pipelineLayout;
        PlantId = plantId;
        Descriptor = descriptor;
    }

    public PlantId PlantId { get; }

    public VulkanGraphicsPipelineDescriptor Descriptor { get; }

    public bool IsDisposed => disposed;

    internal Pipeline NativePipeline { get; private set; }

    internal PipelineLayout NativePipelineLayout { get; private set; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (NativePipeline.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyPipeline(device, NativePipeline, (AllocationCallbacks*)null);
            NativePipeline = default;
        }

        if (NativePipelineLayout.Handle != 0 && device.Handle != 0)
        {
            vk.DestroyPipelineLayout(device, NativePipelineLayout, (AllocationCallbacks*)null);
            NativePipelineLayout = default;
        }

        device = default;
    }
}
