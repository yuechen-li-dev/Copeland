namespace Aurelian.Graphics.Plants;

public sealed record PlantRegistryResult(
    PlantRegistry? Registry,
    IReadOnlyList<PlantRegistryDiagnostic> Diagnostics)
{
    public bool Success => Registry is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != PlantRegistryDiagnosticSeverity.Error);
}
