using Aurelian.Diagnostics;

namespace Aurelian.Core.Engine;

public sealed record AurelianEngineDiagnostic(
    string Code,
    AurelianDiagnosticSeverity Severity,
    string Message);
