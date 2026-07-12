using Aurelian.Runtime.Sessions;

namespace Aurelian.Core.Engine.Runtime;

public interface IAurelianRuntimeTicker
{
    Task<AurelianRuntimeTickResult> TickAsync(
        AurelianRuntimeTickInput input,
        CancellationToken cancellationToken = default);
}
