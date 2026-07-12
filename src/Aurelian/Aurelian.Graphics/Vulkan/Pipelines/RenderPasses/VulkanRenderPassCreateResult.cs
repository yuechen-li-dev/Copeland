namespace Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

public sealed record VulkanRenderPassCreateResult(
    VulkanRenderPassStatus Status,
    AurelianVulkanRenderPass? RenderPass,
    IReadOnlyList<VulkanRenderPassDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanRenderPassStatus.Created && RenderPass is not null;
}
