namespace Aurelian.Graphics.Vulkan.Presentation;

public enum VulkanSwapchainPresentStatus
{
    Presented,
    OutOfDate,
    Suboptimal,
    Unavailable,
    Rejected,
    Failed,
}

public sealed record VulkanSwapchainPresentResult(
    VulkanSwapchainPresentStatus Status,
    IReadOnlyList<VulkanPresentationDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanSwapchainPresentStatus.Presented
        || Status == VulkanSwapchainPresentStatus.Suboptimal;
}
