namespace Aurelian.Rendering.Contracts.Snapshots;

public sealed record RenderCamera2D(
    string Id,
    RenderTransform2 Transform,
    double Width,
    double Height);
