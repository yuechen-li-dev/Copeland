using System.Collections.Generic;
using System.Linq;
using Aurelian.Runtime.Compositor;

namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameResult(
    AurelianFrameStatus Status,
    AurelianFrameId FrameId,
    CompositorPolicyResult? CompositorResult,
    IReadOnlyList<AurelianFrameDiagnostic> Diagnostics)
{
    public bool Success => Status == AurelianFrameStatus.Completed
        && CompositorResult is not null
        && CompositorResult.Success
        && Diagnostics.All(x => x.Severity != AurelianFrameDiagnosticSeverity.Error);
}
