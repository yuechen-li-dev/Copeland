namespace Aurelian.Core.Engine.Graphics;

public enum AurelianPreparedGraphicsSubsystemDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record AurelianPreparedGraphicsSubsystemDiagnostic(
    string Code,
    AurelianPreparedGraphicsSubsystemDiagnosticSeverity Severity,
    string Message);
