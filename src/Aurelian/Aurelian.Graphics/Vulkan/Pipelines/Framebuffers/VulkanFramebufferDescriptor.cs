using Aurelian.Graphics.Vulkan.Resources.Textures;

namespace Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;

public sealed record VulkanFramebufferDescriptor(
    uint Width,
    uint Height,
    IReadOnlyList<AurelianVulkanTexture> ColorAttachments);
