using Aurelian.Core.Engine.Runtime;

namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameLoopIterationResult(
    AurelianFrameId FrameId,
    AurelianRuntimeTickFrameStepResult? RuntimeTickResult,
    AurelianFrameResult FrameResult,
    bool Presented);
