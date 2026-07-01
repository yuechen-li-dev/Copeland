using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Allocation;

public sealed record VulkanMemoryAllocatorTelemetry(
    PlantId PlantId,
    VulkanAllocationBackendKind Backend,
    ulong AllocationCount,
    ulong FreeCount,
    ulong LiveAllocationCount,
    ulong RequestedBytes,
    ulong LiveBytes,
    ulong HighWaterLiveBytes,
    ulong MappedAllocationCount,
    ulong MappedLiveBytes);
