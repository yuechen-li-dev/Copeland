namespace Aurelian.Graphics.Vulkan.Commanding.Submit;

public sealed record VulkanCommandSubmitResult(
    VulkanCommandSubmitStatus Status,
    ulong? SignalFenceValue,
    IReadOnlyList<VulkanCommandSubmitDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanCommandSubmitStatus.Submitted && SignalFenceValue is not null;
}
