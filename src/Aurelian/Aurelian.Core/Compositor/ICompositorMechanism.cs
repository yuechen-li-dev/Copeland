using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Core.Compositor;

public interface ICompositorMechanism
{
    Task<CompositorDispatchResult> DispatchAsync(
        CompositorDispatchRequest request,
        CancellationToken cancellationToken = default);
}
