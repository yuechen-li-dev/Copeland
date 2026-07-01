using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Compositor;

public enum VulkanCompositorDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanCompositorDiagnostic(
    string Code,
    VulkanCompositorDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null);
