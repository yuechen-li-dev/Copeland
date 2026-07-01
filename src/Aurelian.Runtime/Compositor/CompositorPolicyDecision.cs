using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Runtime.Compositor;

public sealed record CompositorPolicyDecision(
    CompositorPolicyKind Policy,
    bool ShouldDispatch,
    CompositorDispatchRequest? Request,
    string Reason);
