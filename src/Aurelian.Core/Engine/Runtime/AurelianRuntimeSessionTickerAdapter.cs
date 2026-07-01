using Aurelian.Runtime.Sessions;

namespace Aurelian.Core.Engine.Runtime;

public sealed class AurelianRuntimeSessionTickerAdapter : IAurelianRuntimeTicker
{
    private readonly AurelianRuntimeSession session;

    public AurelianRuntimeSessionTickerAdapter(AurelianRuntimeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
    }

    public Task<AurelianRuntimeTickResult> TickAsync(
        AurelianRuntimeTickInput input,
        CancellationToken cancellationToken = default) =>
        session.TickAsync(input, cancellationToken);
}
