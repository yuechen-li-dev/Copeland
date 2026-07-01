namespace Aurelian.World.Units;

public sealed record WorldResolutionResult(
    ResolvedWorld? World,
    IReadOnlyList<WorldResolutionDiagnostic> Diagnostics)
{
    public bool Success => World is not null && Diagnostics.All(x => x.Severity != WorldResolutionDiagnosticSeverity.Error);
}
