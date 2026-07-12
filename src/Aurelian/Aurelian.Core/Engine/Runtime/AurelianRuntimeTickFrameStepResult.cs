using System.Linq;
using Aurelian.Core.Engine.Frames;
using Aurelian.Runtime.Sessions;

namespace Aurelian.Core.Engine.Runtime;

public sealed record AurelianRuntimeTickFrameStepResult(
    AurelianRuntimeTickFrameStepStatus Status,
    AurelianFrameId FrameId,
    AurelianRuntimeTickResult? RuntimeResult,
    IReadOnlyList<AurelianRuntimeTickFrameStepDiagnostic> Diagnostics)
{
    public bool Success => Status == AurelianRuntimeTickFrameStepStatus.Ticked
        && RuntimeResult is not null
        && RuntimeResult.Success
        && Diagnostics.All(x => x.Severity != AurelianRuntimeTickFrameStepDiagnosticSeverity.Error);
}
