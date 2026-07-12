namespace Aurelian.Graphics.Vulkan.Resources.Buffers;

public sealed record VulkanBufferCreateResult(
    VulkanBufferStatus Status,
    AurelianVulkanBuffer? Buffer,
    IReadOnlyList<VulkanBufferDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanBufferStatus.Created && Buffer is not null;
}
