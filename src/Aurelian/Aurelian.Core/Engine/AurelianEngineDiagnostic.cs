namespace Aurelian.Core.Engine;

public enum AurelianEngineDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record AurelianEngineDiagnostic(
    string Code,
    AurelianEngineDiagnosticSeverity Severity,
    string Message);
