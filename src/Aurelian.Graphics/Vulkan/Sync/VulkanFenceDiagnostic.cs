using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Sync;

public enum VulkanFenceDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanFenceDiagnostic(
    string Code,
    VulkanFenceDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null,
    string? FenceName = null);
