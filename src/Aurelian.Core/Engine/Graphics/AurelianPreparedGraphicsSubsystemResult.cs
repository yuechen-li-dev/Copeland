namespace Aurelian.Core.Engine.Graphics;

public sealed record AurelianPreparedGraphicsSubsystemResult(
    AurelianPreparedGraphicsSubsystemStatus Status,
    IReadOnlyList<AurelianPreparedGraphicsSubsystemDiagnostic> Diagnostics)
{
    public bool Success => Status == AurelianPreparedGraphicsSubsystemStatus.Valid
        && Diagnostics.All(static diagnostic => diagnostic.Severity != AurelianPreparedGraphicsSubsystemDiagnosticSeverity.Error);
}
