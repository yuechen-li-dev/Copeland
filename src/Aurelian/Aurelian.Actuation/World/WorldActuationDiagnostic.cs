namespace Aurelian.Actuation.World;

public sealed record WorldActuationDiagnostic(
    string Code,
    WorldActuationDiagnosticSeverity Severity,
    string Message);
