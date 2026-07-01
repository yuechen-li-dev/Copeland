using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Rendering.Contracts.Snapshots;

public sealed record RenderSnapshotResult(
    RenderSnapshotStatus Status,
    RenderSnapshot Snapshot,
    IReadOnlyList<RenderSnapshotDiagnostic> Diagnostics)
{
    public bool Success => Status != RenderSnapshotStatus.Rejected
        && Diagnostics.All(x => x.Severity != RenderSnapshotDiagnosticSeverity.Error);
}
