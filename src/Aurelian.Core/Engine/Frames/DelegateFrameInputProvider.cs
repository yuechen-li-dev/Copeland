namespace Aurelian.Core.Engine.Frames;

public sealed class DelegateFrameInputProvider : IAurelianFrameInputProvider
{
    private readonly Func<AurelianFrameId, CancellationToken, ValueTask<AurelianFrameInput?>> next;

    public DelegateFrameInputProvider(
        Func<AurelianFrameId, CancellationToken, ValueTask<AurelianFrameInput?>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        this.next = next;
    }

    public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(
        AurelianFrameId frameId,
        CancellationToken cancellationToken = default) =>
        next(frameId, cancellationToken);
}
