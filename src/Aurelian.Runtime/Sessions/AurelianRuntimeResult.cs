using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Runtime.Sessions;

public sealed record AurelianRuntimeResult(
    bool Success,
    IReadOnlyList<AurelianRuntimeDiagnostic> Diagnostics)
{
    public static AurelianRuntimeResult Ok() => new(true, []);

    public static AurelianRuntimeResult Rejected(AurelianRuntimeDiagnostic diagnostic) => new(false, [diagnostic]);

    public bool HasErrors => Diagnostics.Any(x => x.Severity == AurelianRuntimeDiagnosticSeverity.Error);
}
