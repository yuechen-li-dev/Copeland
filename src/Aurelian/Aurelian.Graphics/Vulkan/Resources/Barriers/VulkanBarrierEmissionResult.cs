namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanBarrierEmissionResult(
    VulkanBarrierEmissionStatus Status,
    int ImageBarrierCount,
    int BufferBarrierCount,
    IReadOnlyList<VulkanBarrierDiagnostic> Diagnostics)
{
    public bool Success => Status != VulkanBarrierEmissionStatus.Rejected
        && Status != VulkanBarrierEmissionStatus.Failed
        && Diagnostics.All(x => x.Severity != VulkanBarrierDiagnosticSeverity.Error);
}
