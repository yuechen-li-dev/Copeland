using Aurelian.World.Units;

namespace Aurelian.Actuation.World;

public sealed record WorldActuationResult(
    WorldActuationStatus Status,
    WorldDocument Document,
    IReadOnlyList<WorldActuationDiagnostic> Diagnostics)
{
    public bool Applied => Status == WorldActuationStatus.Applied;

    public bool Success => Status != WorldActuationStatus.Rejected
        && Diagnostics.All(x => x.Severity != WorldActuationDiagnosticSeverity.Error);
}

public sealed record WorldActuationResult<TDocument>(
    WorldActuationStatus Status,
    TDocument Document,
    IReadOnlyList<WorldActuationDiagnostic> Diagnostics)
{
    public bool Applied => Status == WorldActuationStatus.Applied;

    public bool Success => Status != WorldActuationStatus.Rejected
        && Diagnostics.All(x => x.Severity != WorldActuationDiagnosticSeverity.Error);
}
