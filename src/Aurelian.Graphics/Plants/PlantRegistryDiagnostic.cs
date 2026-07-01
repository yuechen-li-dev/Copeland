namespace Aurelian.Graphics.Plants;

public enum PlantRegistryDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record PlantRegistryDiagnostic(
    string Code,
    PlantRegistryDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null);
