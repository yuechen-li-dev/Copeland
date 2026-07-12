namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public enum VulkanBarrierDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record VulkanBarrierDiagnostic(
    string Code,
    VulkanBarrierDiagnosticSeverity Severity,
    string Message,
    string? ResourceName = null,
    uint? MipLevel = null,
    uint? ArrayLayer = null);
