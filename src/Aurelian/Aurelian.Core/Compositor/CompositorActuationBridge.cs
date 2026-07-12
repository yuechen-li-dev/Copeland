using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;

namespace Aurelian.Core.Compositor;

public sealed class CompositorActuationBridge
{
    private readonly ICompositorMechanism _mechanism;

    public CompositorActuationBridge(ICompositorMechanism mechanism)
    {
        ArgumentNullException.ThrowIfNull(mechanism);
        _mechanism = mechanism;
    }

    public Task<CompositorDispatchResult> HandleAsync(
        CompositorDispatchAct act,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(act.Request);

        return _mechanism.DispatchAsync(act.Request, cancellationToken);
    }

    public Func<CompositorDispatchAct, CancellationToken, Task<CompositorDispatchResult>> AsHandler() => HandleAsync;
}
