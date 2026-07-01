using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Runtime.Sessions;

public sealed record AurelianRuntimeTickResult(
    AurelianRuntimeTickStatus Status,
    ulong TickIndex,
    TimeSpan DeltaTime,
    IReadOnlyList<AurelianRuntimeDiagnostic> Diagnostics)
{
    public bool Success => Status == AurelianRuntimeTickStatus.Ticked
        && Diagnostics.All(x => x.Severity != AurelianRuntimeDiagnosticSeverity.Error);
}
