using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Compositor;

public static class VulkanPresentationTargetResolver
{
    public static VulkanPresentationTargetResolutionResult Resolve(
        VulkanPresentationTargetImageSet? imageSet,
        PresentationTargetRef target)
    {
        if (imageSet is null)
        {
            return Rejected([
                new VulkanPresentationTargetDiagnostic(
                    VulkanPresentationTargetDiagnosticCodes.ImageSetMissing,
                    VulkanPresentationTargetDiagnosticSeverity.Error,
                    "Cannot resolve a presentation target without a presentation target image set.",
                    null,
                    target.SwapchainImageIndex),
            ]);
        }

        if (target.PlantId != imageSet.PlantId.Value)
        {
            return Rejected([
                new VulkanPresentationTargetDiagnostic(
                    VulkanPresentationTargetDiagnosticCodes.PlantMismatch,
                    VulkanPresentationTargetDiagnosticSeverity.Error,
                    $"Presentation target plant {target.PlantId} does not match image set plant {imageSet.PlantId.Value}.",
                    imageSet.PlantId,
                    target.SwapchainImageIndex),
            ]);
        }

        if (!imageSet.TryGet(target.SwapchainImageIndex, out VulkanPresentationTargetImage resolved))
        {
            return Rejected([
                new VulkanPresentationTargetDiagnostic(
                    VulkanPresentationTargetDiagnosticCodes.ImageIndexOutOfRange,
                    VulkanPresentationTargetDiagnosticSeverity.Error,
                    $"Swapchain image index {target.SwapchainImageIndex} is outside the image set range 0 through {Math.Max(0, imageSet.Images.Count - 1)}.",
                    imageSet.PlantId,
                    target.SwapchainImageIndex),
            ]);
        }

        return new VulkanPresentationTargetResolutionResult(VulkanPresentationTargetStatus.Resolved, resolved, []);
    }

    private static VulkanPresentationTargetResolutionResult Rejected(IReadOnlyList<VulkanPresentationTargetDiagnostic> diagnostics)
        => new(VulkanPresentationTargetStatus.Rejected, null, diagnostics);
}
