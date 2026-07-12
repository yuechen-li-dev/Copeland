using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Compositor;

public sealed record VulkanPresentationTargetDiagnostic(
    string Code,
    VulkanPresentationTargetDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null,
    uint? SwapchainImageIndex = null);

public enum VulkanPresentationTargetDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}
