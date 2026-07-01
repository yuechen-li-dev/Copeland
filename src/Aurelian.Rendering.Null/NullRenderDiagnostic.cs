namespace Aurelian.Rendering.Null;

public enum NullRenderDiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed record NullRenderDiagnostic(
    string Code,
    NullRenderDiagnosticSeverity Severity,
    string Message);
