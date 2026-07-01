namespace Aurelian.Core.Engine.Graphics;

public interface IPresentationMechanism
{
    Task PresentAsync(CancellationToken cancellationToken = default);
}
