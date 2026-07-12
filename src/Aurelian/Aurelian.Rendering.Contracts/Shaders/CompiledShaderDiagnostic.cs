namespace Aurelian.Rendering.Contracts.Shaders;

public enum CompiledShaderDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record CompiledShaderDiagnostic(
    string Code,
    CompiledShaderDiagnosticSeverity Severity,
    string Message,
    CompiledShaderStageKind? Stage = null);
