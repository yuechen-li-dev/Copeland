using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public sealed record VulkanRenderPassCommandDiagnostic(
    string Code,
    VulkanRenderPassCommandDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null);

public enum VulkanRenderPassCommandDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}
