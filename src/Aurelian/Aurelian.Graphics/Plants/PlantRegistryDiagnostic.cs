namespace Aurelian.Graphics.Plants;

public sealed record PlantRegistryDiagnostic(
    string Code,
    Aurelian.Diagnostics.AurelianDiagnosticSeverity Severity,
    string Message,
    PlantId? PlantId = null);
