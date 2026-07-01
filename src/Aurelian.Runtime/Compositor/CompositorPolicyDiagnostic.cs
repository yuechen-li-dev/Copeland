namespace Aurelian.Runtime.Compositor;

public sealed record CompositorPolicyDiagnostic(
    string Code,
    CompositorPolicyDiagnosticSeverity Severity,
    string Message);
