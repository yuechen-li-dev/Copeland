using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Sessions;

public sealed record AurelianRuntimeSessionOptions
{
    public ActuatorHost? ActuatorHost { get; init; }
    public AiWorld? World { get; init; }
    public IAurelianAiWorldRunner? Runner { get; init; }
    public Action<ActuatorHost>? ConfigureActuatorHost { get; init; }
}
