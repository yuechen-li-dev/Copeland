namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanBarrierPlan(
    string ResourceName,
    VulkanResourceLayout OldLayout,
    VulkanResourceLayout NewLayout,
    VulkanBarrierMapping OldMapping,
    VulkanBarrierMapping NewMapping,
    uint BaseMipLevel = 0,
    uint LevelCount = 1,
    uint BaseArrayLayer = 0,
    uint LayerCount = 1);
