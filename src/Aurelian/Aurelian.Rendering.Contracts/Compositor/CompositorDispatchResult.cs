using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Rendering.Contracts.Compositor;

public sealed record CompositorDispatchResult(
    CompositorDispatchStatus Status,
    ulong FrameId,
    CompositorPolicyKind Policy,
    PresentationTargetRef Target,
    CompositorDiagnostics Diagnostics,
    IReadOnlyList<CompositorDispatchDiagnostic> DispatchDiagnostics)
{
    public bool Success => Status == CompositorDispatchStatus.Dispatched
        && DispatchDiagnostics.All(x => x.Severity != CompositorDispatchDiagnosticSeverity.Error);
}
