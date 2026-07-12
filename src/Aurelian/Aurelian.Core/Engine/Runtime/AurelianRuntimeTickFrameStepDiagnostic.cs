namespace Aurelian.Core.Engine.Runtime;

public sealed record AurelianRuntimeTickFrameStepDiagnostic(
    string Code,
    AurelianRuntimeTickFrameStepDiagnosticSeverity Severity,
    string Message);
