namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public enum VulkanCompiledGraphicsPipelineDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanCompiledGraphicsPipelineDiagnostic(
    string Code,
    VulkanCompiledGraphicsPipelineDiagnosticSeverity Severity,
    string Message,
    VulkanShaderStageKind? Stage = null);
