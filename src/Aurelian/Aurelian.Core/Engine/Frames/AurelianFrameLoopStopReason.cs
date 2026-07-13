namespace Aurelian.Core.Engine.Frames;

public enum AurelianFrameLoopStopReason
{
    MaxFramesReached,
    InputProviderCompleted,
    CloseRequested,
    FrameFailed,
    Cancelled,
    Rejected,
}
