namespace Aurelian.Graphics.Vulkan.Presentation;

public enum VulkanSwapchainAcquireStatus
{
    Acquired,
    OutOfDate,
    Suboptimal,
    Unavailable,
    Rejected,
    Failed,
    /// <summary>No image was free within the (zero) timeout; nothing was acquired and the fence is untouched.</summary>
    NotReady,
}

public sealed record VulkanSwapchainAcquireResult(
    VulkanSwapchainAcquireStatus Status,
    uint? ImageIndex,
    IReadOnlyList<VulkanPresentationDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanSwapchainAcquireStatus.Acquired && ImageIndex is not null;
}
