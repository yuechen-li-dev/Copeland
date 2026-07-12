namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanSubresourceRange(
    uint BaseMipLevel = 0,
    uint LevelCount = 1,
    uint BaseArrayLayer = 0,
    uint LayerCount = 1);
