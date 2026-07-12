using System.Collections.Generic;
using System.Linq;
using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Runtime.Compositor;

public sealed record CompositorPolicyResult(
    CompositorPolicyStatus Status,
    CompositorPolicyDecision Decision,
    CompositorDispatchResult? DispatchResult,
    IReadOnlyList<CompositorPolicyDiagnostic> Diagnostics)
{
    public bool Success => Status == CompositorPolicyStatus.Dispatched
        && DispatchResult is not null
        && DispatchResult.Success
        && Diagnostics.All(x => x.Severity != CompositorPolicyDiagnosticSeverity.Error);
}
