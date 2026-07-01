using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Presentation;

public sealed record VulkanPresentationDiagnostic(
    string Code,
    VulkanPresentationDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null);

public enum VulkanPresentationDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}
