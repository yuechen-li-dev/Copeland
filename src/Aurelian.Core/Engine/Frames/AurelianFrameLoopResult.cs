using System.Linq;

namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameLoopResult(
    AurelianFrameLoopStatus Status,
    AurelianFrameLoopStopReason StopReason,
    int FramesAttempted,
    int FramesCompleted,
    IReadOnlyList<AurelianFrameLoopIterationResult> Iterations,
    IReadOnlyList<AurelianFrameLoopDiagnostic> Diagnostics)
{
    public bool Success => Status == AurelianFrameLoopStatus.Completed
        && Diagnostics.All(x => x.Severity != AurelianFrameLoopDiagnosticSeverity.Error);
}
