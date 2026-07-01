namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameLoopOptions(
    int? MaxFrames = 1,
    bool PresentAfterCompletedFrame = true,
    bool StopOnFrameFailure = true,
    TimeSpan DefaultDeltaTime = default)
{
    public TimeSpan RuntimeDeltaTime => DefaultDeltaTime == default
        ? TimeSpan.FromSeconds(1.0 / 60.0)
        : DefaultDeltaTime;
}
