using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed record VulkanSwapchainFacts(
    PlantId PlantId,
    uint Width,
    uint Height,
    string SelectedFormat,
    string SelectedColorSpace,
    string SelectedPresentMode,
    uint ImageCount,
    uint ImageViewCount,
    uint CurrentTransform);
