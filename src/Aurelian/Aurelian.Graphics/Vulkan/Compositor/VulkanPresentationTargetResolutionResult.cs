namespace Aurelian.Graphics.Vulkan.Compositor;

public sealed record VulkanPresentationTargetResolutionResult(
    VulkanPresentationTargetStatus Status,
    VulkanPresentationTargetImage? Target,
    IReadOnlyList<VulkanPresentationTargetDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanPresentationTargetStatus.Resolved && Target is not null;
}
