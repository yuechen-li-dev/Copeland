using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

public enum VulkanRenderPassDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanRenderPassDiagnostic(
    string Code,
    VulkanRenderPassDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    string? AttachmentName = null);
