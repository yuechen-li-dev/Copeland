using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Resources.Allocation;

public sealed record VulkanAllocationRequest(
    PlantId PlantId,
    ulong SizeBytes,
    uint MemoryTypeBits,
    VulkanMemoryUsage Usage,
    string DebugName,
    bool MapOnCreate = false);
