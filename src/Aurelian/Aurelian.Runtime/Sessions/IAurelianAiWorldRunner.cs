using Dominatus.Core.Runtime;
using Aurelian.Runtime.Sessions;

namespace Aurelian.Runtime.Dominatus;

/// <summary>
/// Advanced Dominatus extension point for authors who deliberately own a
/// Dominatus world. Ordinary Aurelian sessions use the built-in sequential
/// runner and should not implement this interface.
/// </summary>
public interface IAurelianDominatusWorldRunner
{
    Task RunTickAsync(
        AiWorld world,
        AurelianRuntimeTickInput input,
        CancellationToken cancellationToken = default);
}
