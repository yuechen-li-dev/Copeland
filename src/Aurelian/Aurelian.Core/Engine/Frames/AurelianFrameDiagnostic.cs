using Aurelian.Diagnostics;

namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameDiagnostic(
    string Code,
    AurelianDiagnosticSeverity Severity,
    string Message);
