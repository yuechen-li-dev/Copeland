namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed record VulkanSwapchainCreateResult(
    VulkanPresentationStatus Status,
    AurelianVulkanSurface? Surface,
    AurelianVulkanSwapchain? Swapchain,
    IReadOnlyList<VulkanPresentationDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanPresentationStatus.Created && Surface is not null && Swapchain is not null;
}
