using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public enum VulkanGraphicsPipelineDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanGraphicsPipelineDiagnostic(
    string Code,
    VulkanGraphicsPipelineDiagnosticSeverity Severity,
    string Message,
    PlantId PlantId,
    VulkanShaderStageKind? Stage = null);
