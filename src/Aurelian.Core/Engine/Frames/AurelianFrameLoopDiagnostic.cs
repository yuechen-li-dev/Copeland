namespace Aurelian.Core.Engine.Frames;

public enum AurelianFrameLoopDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record AurelianFrameLoopDiagnostic(
    string Code,
    AurelianFrameLoopDiagnosticSeverity Severity,
    string Message);
