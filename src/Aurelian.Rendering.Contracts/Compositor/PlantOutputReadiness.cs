namespace Aurelian.Rendering.Contracts.Compositor;

public sealed record PlantOutputReadiness(
    PlantOutputRef Output,
    PlantOutputReadinessStatus Status,
    ulong? CompletedFenceValue = null,
    string? DiagnosticCode = null);
