namespace Aurelian.Core.Engine.Graphics;

public sealed record AurelianPreparedGraphicsSubsystemDiagnostic(
    string Code,
    Aurelian.Diagnostics.AurelianDiagnosticSeverity Severity,
    string Message);
