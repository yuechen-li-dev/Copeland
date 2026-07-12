namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed record VulkanPresentationResult(
    VulkanPresentationStatus Status,
    IReadOnlyList<VulkanPresentationDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanPresentationStatus.Presented;
}
