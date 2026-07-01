using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;

public enum VulkanFramebufferDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanFramebufferDiagnostic(
    string Code,
    VulkanFramebufferDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    string? AttachmentName = null);
