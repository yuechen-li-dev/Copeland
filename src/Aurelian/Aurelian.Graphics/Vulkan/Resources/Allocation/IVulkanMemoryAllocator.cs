using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Allocation;

public interface IVulkanMemoryAllocator : IDisposable
{
    PlantId PlantId { get; }

    VulkanMemoryAllocatorTelemetry Telemetry { get; }

    VulkanAllocationResult Allocate(VulkanAllocationRequest request);
}
