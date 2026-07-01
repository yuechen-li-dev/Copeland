using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Diagnostics;

public enum VulkanInitDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanInitDiagnostic(
    string Code,
    VulkanInitDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null);
