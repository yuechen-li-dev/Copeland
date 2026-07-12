using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Commanding;

public sealed record VulkanCommandBufferDiagnostic(
    string Code,
    VulkanCommandBufferDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId);

public enum VulkanCommandBufferDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}
