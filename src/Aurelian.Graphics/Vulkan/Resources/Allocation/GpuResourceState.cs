using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Allocation;

public sealed record GpuResourceState(
    PlantId PlantId,
    ulong SizeBytes,
    VulkanAllocationBackendKind AllocationBackend,
    ulong? RetireFenceValue = null);
