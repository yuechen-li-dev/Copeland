namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed record VulkanGraphicsPipelineCreateResult(
    VulkanGraphicsPipelineStatus Status,
    AurelianVulkanGraphicsPipeline? Pipeline,
    IReadOnlyList<VulkanGraphicsPipelineDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanGraphicsPipelineStatus.Created && Pipeline is not null;
}
