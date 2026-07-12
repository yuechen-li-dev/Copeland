namespace Aurelian.Runtime.Sessions;

public sealed record AurelianRuntimeDiagnostic(
    string Code,
    AurelianRuntimeDiagnosticSeverity Severity,
    string Message);
