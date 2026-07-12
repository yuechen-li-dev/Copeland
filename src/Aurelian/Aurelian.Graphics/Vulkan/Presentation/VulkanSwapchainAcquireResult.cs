namespace Aurelian.Graphics.Vulkan.Presentation;

public enum VulkanSwapchainAcquireStatus
{
    Acquired,
    OutOfDate,
    Suboptimal,
    Unavailable,
    Rejected,
    Failed,
}

public sealed record VulkanSwapchainAcquireResult(
    VulkanSwapchainAcquireStatus Status,
    uint? ImageIndex,
    IReadOnlyList<VulkanPresentationDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanSwapchainAcquireStatus.Acquired && ImageIndex is not null;
}
