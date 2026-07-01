using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Core.Engine;

public sealed record AurelianEngineResult(
    AurelianEngineStatus Status,
    IReadOnlyList<AurelianEngineDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(static diagnostic => diagnostic.Severity != AurelianEngineDiagnosticSeverity.Error);

    public static AurelianEngineResult Successful(AurelianEngineStatus status) => new(status, []);

    public static AurelianEngineResult Failed(AurelianEngineStatus status, AurelianEngineDiagnostic diagnostic) => new(status, [diagnostic]);
}
