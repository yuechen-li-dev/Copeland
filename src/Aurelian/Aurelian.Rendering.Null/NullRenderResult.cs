using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Rendering.Null;

public sealed record NullRenderResult(
    NullRenderStatus Status,
    NullRenderTrace Trace,
    IReadOnlyList<NullRenderDiagnostic> Diagnostics)
{
    public bool Success => Status != NullRenderStatus.Rejected
        && Diagnostics.All(x => x.Severity != NullRenderDiagnosticSeverity.Error);
}
