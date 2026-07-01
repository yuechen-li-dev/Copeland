using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Compositor;

public enum VulkanPlantOutputDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanPlantOutputDiagnostic(
    string Code,
    VulkanPlantOutputDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null,
    string? ImageId = null);
