using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Commanding.Draw;

public enum VulkanDrawCommandDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanDrawCommandDiagnostic(
    string Code,
    VulkanDrawCommandDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId);
