using Aurelian.Graphics.Vulkan.Compositor;

namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanPresentationTargetBarrierEmission(
    VulkanPresentationTargetImage Target,
    VulkanBarrierPlan Plan);
