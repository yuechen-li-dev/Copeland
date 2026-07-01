namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public sealed record VulkanCompiledGraphicsPipelineDescriptorResult(
    VulkanCompiledGraphicsPipelineStatus Status,
    VulkanGraphicsPipelineDescriptor? Descriptor,
    IReadOnlyList<VulkanCompiledGraphicsPipelineDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanCompiledGraphicsPipelineStatus.Created && Descriptor is not null;
}
