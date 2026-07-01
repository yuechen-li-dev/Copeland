using Aurelian.Rendering.Contracts.Compositor;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Compositor;

internal static class CompositorPolicyKeys
{
    public static readonly BbKey<CompositorPolicyFacts> Facts = new("aurelian.compositor.policy.facts");
    public static readonly BbKey<CompositorPolicyDecision> Decision = new("aurelian.compositor.policy.decision");
    public static readonly BbKey<ActuationId> DispatchActuationId = new("aurelian.compositor.policy.dispatchActuationId");
    public static readonly BbKey<CompositorDispatchResult> DispatchResult = new("aurelian.compositor.policy.dispatchResult");
}
