namespace Aurelian.Core.Engine.Frames;

public interface IAurelianFrameInputProvider
{
    ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(
        AurelianFrameId frameId,
        CancellationToken cancellationToken = default);
}
