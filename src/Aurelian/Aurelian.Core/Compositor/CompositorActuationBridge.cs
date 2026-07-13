using Aurelian.Rendering.Contracts.Compositor;

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
        CompositorDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _mechanism.DispatchAsync(request, cancellationToken);
    }

    public Func<CompositorDispatchRequest, CancellationToken, Task<CompositorDispatchResult>> AsHandler() => HandleAsync;
}
