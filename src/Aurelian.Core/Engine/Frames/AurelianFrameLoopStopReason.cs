namespace Aurelian.Core.Engine.Frames;

public enum AurelianFrameLoopStopReason
{
    MaxFramesReached,
    InputProviderCompleted,
    FrameFailed,
    Cancelled,
    Rejected,
}
