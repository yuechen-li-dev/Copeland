using Aurelian.Graphics.Vulkan.Resources.Textures;

namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanTextureBarrierEmission(
    AurelianVulkanTexture Texture,
    VulkanBarrierPlan Plan);
