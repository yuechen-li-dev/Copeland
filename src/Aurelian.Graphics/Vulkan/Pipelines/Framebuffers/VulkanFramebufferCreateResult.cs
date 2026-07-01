namespace Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;

public sealed record VulkanFramebufferCreateResult(
    VulkanFramebufferStatus Status,
    AurelianVulkanFramebuffer? Framebuffer,
    IReadOnlyList<VulkanFramebufferDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanFramebufferStatus.Created && Framebuffer is not null;
}
