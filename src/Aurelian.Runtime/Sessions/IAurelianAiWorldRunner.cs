using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Sessions;

public interface IAurelianAiWorldRunner
{
    Task RunTickAsync(
        AiWorld world,
        AurelianRuntimeTickInput input,
        CancellationToken cancellationToken = default);
}
