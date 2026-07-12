namespace Aurelian.Graphics.Vulkan.Resources;

public enum ResourcePoolDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record ResourcePoolDiagnostic(
    string Code,
    ResourcePoolDiagnosticSeverity Severity,
    string Message);
