namespace Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

public sealed record VulkanRenderPassDescriptor(
    IReadOnlyList<VulkanRenderPassAttachmentDescriptor> ColorAttachments);
