namespace Aurelian.Actuation.World;

public sealed record WorldActuationDiagnostic(
    string Code,
    Aurelian.Diagnostics.AurelianDiagnosticSeverity Severity,
    string Message);
