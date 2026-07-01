using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Commanding.Submit;

public enum VulkanCommandSubmitDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanCommandSubmitDiagnostic(
    string Code,
    VulkanCommandSubmitDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    string? DebugName = null);
