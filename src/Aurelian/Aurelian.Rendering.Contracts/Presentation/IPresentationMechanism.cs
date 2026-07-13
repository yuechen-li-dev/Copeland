namespace Aurelian.Rendering.Contracts.Presentation;

/// <summary>
/// Presents a completed renderer frame through a prepared backend target.
/// </summary>
public interface IPresentationMechanism
{
    Task PresentAsync(CancellationToken cancellationToken = default);
}
