using System.Linq;
using Aurelian.Core.Engine.Frames;
using Aurelian.Diagnostics;
using Aurelian.Runtime.Sessions;

namespace Aurelian.Core.Engine.Runtime;

public enum AurelianRuntimeTickFrameStepStatus
{
    Ticked,
    Rejected,
    Failed,
    Cancelled,
}

public static class AurelianRuntimeTickFrameStepDiagnosticCodes
{
    public const string RuntimeTickerMissing = "ACRT1001";
    public const string InvalidDeltaTime = "ACRT1002";
    public const string RuntimeTickRejected = "ACRT1003";
    public const string RuntimeTickFailed = "ACRT1004";
    public const string RuntimeTickCancelled = "ACRT1005";
}

public sealed record AurelianRuntimeTickFrameStepDiagnostic(
    string Code,
    AurelianDiagnosticSeverity Severity,
    string Message);

public sealed record AurelianRuntimeTickFrameStepResult(
    AurelianRuntimeTickFrameStepStatus Status,
    AurelianFrameId FrameId,
    AurelianRuntimeTickResult? RuntimeResult,
    IReadOnlyList<AurelianRuntimeTickFrameStepDiagnostic> Diagnostics)
{
    public bool Success => Status == AurelianRuntimeTickFrameStepStatus.Ticked
        && RuntimeResult is not null
        && RuntimeResult.Success
        && Diagnostics.All(diagnostic => diagnostic.Severity != AurelianDiagnosticSeverity.Error);
}
