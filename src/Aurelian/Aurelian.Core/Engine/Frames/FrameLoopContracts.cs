using System.Linq;
using Aurelian.Core.Engine.Runtime;
using Aurelian.Diagnostics;

namespace Aurelian.Core.Engine.Frames;

public enum AurelianFrameLoopStatus
{
    Completed,
    Cancelled,
    Rejected,
    Failed,
}

public enum AurelianFrameLoopStopReason
{
    MaxFramesReached,
    InputProviderCompleted,
    CloseRequested,
    FrameFailed,
    Cancelled,
    Rejected,
}

public sealed record AurelianFrameLoopOptions(
    int? MaxFrames = 1,
    bool PresentAfterCompletedFrame = true,
    bool StopOnFrameFailure = true,
    TimeSpan DefaultDeltaTime = default,
    int MaxRetainedDiagnostics = 64)
{
    public TimeSpan RuntimeDeltaTime => DefaultDeltaTime == default
        ? TimeSpan.FromSeconds(1.0 / 60.0)
        : DefaultDeltaTime;
}

public static class AurelianFrameLoopDiagnosticCodes
{
    public const string FramePumpMissing = "ACFL1001";
    public const string InputProviderMissing = "ACFL1002";
    public const string InvalidMaxFrames = "ACFL1003";
    public const string FrameInputMissing = "ACFL1004";
    public const string FrameFailed = "ACFL1005";
    public const string PresentationFailed = "ACFL1006";
    public const string Cancelled = "ACFL1007";
    public const string RuntimeTickFailed = "ACFL1008";
    public const string RuntimeTickRejected = "ACFL1009";
    public const string RuntimeTickCancelled = "ACFL1010";
    public const string CloseAccepted = "ACFL1011";
    public const string CloseRejected = "ACFL1012";
    public const string HarnessRequiresFiniteMaxFrames = "ACFL1013";
    public const string InvalidDiagnosticCapacity = "ACFL1014";
}

public sealed record AurelianFrameLoopDiagnostic(
    string Code,
    AurelianDiagnosticSeverity Severity,
    string Message);

public sealed record AurelianFrameLoopResult(
    AurelianFrameLoopStatus Status,
    AurelianFrameLoopStopReason StopReason,
    int FramesAttempted,
    int FramesCompleted,
    IReadOnlyList<AurelianFrameLoopDiagnostic> Diagnostics,
    int DroppedDiagnosticCount = 0)
{
    public bool Success => Status == AurelianFrameLoopStatus.Completed
        && Diagnostics.All(diagnostic => diagnostic.Severity != AurelianDiagnosticSeverity.Error);
}

public sealed record AurelianFrameLoopIterationResult(
    AurelianFrameId FrameId,
    AurelianRuntimeTickFrameStepResult? RuntimeTickResult,
    AurelianFrameResult FrameResult,
    bool Presented);

/// <summary>
/// Explicit finite evidence result. Unlike <see cref="AurelianFrameLoopResult"/>,
/// this result intentionally retains one immutable record per attempted frame.
/// </summary>
public sealed record AurelianFrameLoopHarnessResult(
    AurelianFrameLoopResult Completion,
    IReadOnlyList<AurelianFrameLoopIterationResult> Iterations)
{
    public bool Success => Completion.Success;
    public AurelianFrameLoopStatus Status => Completion.Status;
    public AurelianFrameLoopStopReason StopReason => Completion.StopReason;
    public int FramesAttempted => Completion.FramesAttempted;
    public int FramesCompleted => Completion.FramesCompleted;
    public IReadOnlyList<AurelianFrameLoopDiagnostic> Diagnostics => Completion.Diagnostics;
}

/// <summary>
/// Receives transient iteration evidence without making the production loop its owner.
/// Implementations must define and bound their own retention policy.
/// </summary>
public interface IAurelianFrameLoopIterationSink
{
    void OnIteration(AurelianFrameLoopIterationResult iteration);
}
