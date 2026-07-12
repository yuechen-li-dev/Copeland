namespace Aurelian.World.Units;

public sealed record WorldResolutionDiagnostic(
    string Code,
    WorldResolutionDiagnosticSeverity Severity,
    string Message,
    UnitId? UnitId = null,
    UnitId? RelatedUnitId = null);
