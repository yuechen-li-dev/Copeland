using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Dominatus;

/// <summary>
/// Explicit advanced configuration for a session backed by caller-supplied
/// Dominatus runtime objects. Use this only when custom Dominatus orchestration
/// is intentional; ordinary Aurelian consumers should use the parameterless
/// <c>AurelianRuntimeSession</c> constructor.
/// </summary>
public sealed record AurelianRuntimeDominatusOptions
{
    /// <summary>
    /// Optional host used by a caller-supplied world, or used to create the
    /// session's world when <see cref="World"/> is not supplied.
    /// </summary>
    public ActuatorHost? ActuatorHost { get; init; }

    /// <summary>
    /// Optional caller-owned world. It must use <see cref="ActuatorHost"/>,
    /// when one is supplied, or an <see cref="ActuatorHost"/> actuator.
    /// </summary>
    public AiWorld? World { get; init; }

    /// <summary>
    /// Optional caller-owned world runner. Its invocation remains on the
    /// session tick path and must honour the supplied cancellation token.
    /// </summary>
    public IAurelianDominatusWorldRunner? WorldRunner { get; init; }

    /// <summary>
    /// Adds advanced Dominatus handlers during <c>Start</c>, before the session
    /// creates its runtime agent. Do not mutate the host while a tick is active.
    /// </summary>
    public Action<ActuatorHost>? ConfigureActuatorHost { get; init; }
}
