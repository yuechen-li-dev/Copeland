namespace Aurelian.Core.Engine.Frames;

public enum AurelianFrameDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

public sealed record AurelianFrameDiagnostic(
    string Code,
    AurelianFrameDiagnosticSeverity Severity,
    string Message);
