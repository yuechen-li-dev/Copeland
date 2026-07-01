using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed record VulkanSurfaceFacts(
    PlantId PlantId,
    uint Width,
    uint Height,
    uint CurrentTransform,
    uint MinImageCount,
    uint MaxImageCount);
