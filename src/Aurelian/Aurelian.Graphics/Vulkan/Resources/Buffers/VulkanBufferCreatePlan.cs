using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Resources.Allocation;

namespace Aurelian.Graphics.Vulkan.Resources.Buffers;

public sealed record VulkanBufferCreatePlan(
    PlantId PlantId,
    ulong SizeBytes,
    VulkanBufferUsage Usage,
    VulkanMemoryUsage MemoryUsage,
    string DebugName,
    bool MapOnCreate = false);
