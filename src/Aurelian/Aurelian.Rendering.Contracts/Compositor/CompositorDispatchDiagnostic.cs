namespace Aurelian.Rendering.Contracts.Compositor;

public enum CompositorDispatchDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record CompositorDispatchDiagnostic(
    string Code,
    CompositorDispatchDiagnosticSeverity Severity,
    string Message);
