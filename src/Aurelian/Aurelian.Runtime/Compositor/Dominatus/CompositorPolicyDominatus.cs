using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Dominatus;

/// <summary>
/// Explicit advanced entry point for hosts that deliberately supply a
/// Dominatus actuator host to Aurelian compositor policy. Ordinary frame-pump
/// consumers should use the Aurelian-owned dispatch delegate on
/// <see cref="Compositor.CompositorPolicySession"/> instead.
/// The caller owns host configuration and must not mutate it during a policy
/// tick; trace and persistence behavior follows that Dominatus host.
/// </summary>
public static class CompositorPolicyDominatus
{
    public static Task<Compositor.CompositorPolicyResult> RunOnceAsync(
        Compositor.CompositorPolicyFacts facts,
        ActuatorHost actuatorHost,
        CancellationToken cancellationToken = default)
    {
        return Compositor.CompositorPolicySession.RunOnceWithActuatorHostAsync(
            facts,
            actuatorHost,
            cancellationToken);
    }
}
