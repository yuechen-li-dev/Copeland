namespace Aurelian.Rendering.Contracts.Compositor;

/// <summary>
/// Executes a renderer-specific compositor mechanism from a neutral dispatch request.
/// </summary>
public interface ICompositorMechanism
{
    Task<CompositorDispatchResult> DispatchAsync(
        CompositorDispatchRequest request,
        CancellationToken cancellationToken = default);
}
