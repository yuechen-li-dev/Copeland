using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Dominatus;

/// <summary>
/// Explicit inspection and advanced-composition access for a runtime session.
/// The session owns both objects for its full lifetime. Callers may configure
/// them before <c>Start</c>; after start they must not mutate world topology,
/// handlers, blackboards, tracing, or persistence while a tick is active.
/// Persistence and trace policy remain caller-owned when this access path is
/// chosen, and compatibility follows the referenced Dominatus package.
/// </summary>
public sealed class AurelianRuntimeDominatusAccess
{
    internal AurelianRuntimeDominatusAccess(AiWorld world, ActuatorHost actuatorHost)
    {
        World = world;
        ActuatorHost = actuatorHost;
    }

    public AiWorld World { get; }

    public ActuatorHost ActuatorHost { get; }
}
