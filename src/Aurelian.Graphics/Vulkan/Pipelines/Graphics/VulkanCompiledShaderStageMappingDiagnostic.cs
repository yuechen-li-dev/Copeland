namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public enum VulkanCompiledShaderStageMappingDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record VulkanCompiledShaderStageMappingDiagnostic(
    string Code,
    VulkanCompiledShaderStageMappingDiagnosticSeverity Severity,
    string Message,
    VulkanShaderStageKind? Stage = null);
