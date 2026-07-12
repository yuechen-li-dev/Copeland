namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed record VulkanCompiledGraphicsPipelineCreateResult(
    VulkanCompiledGraphicsPipelineStatus Status,
    AurelianVulkanGraphicsPipeline? Pipeline,
    VulkanGraphicsPipelineDescriptor? Descriptor,
    IReadOnlyList<VulkanCompiledGraphicsPipelineDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanCompiledGraphicsPipelineStatus.Created && Pipeline is not null;
}
