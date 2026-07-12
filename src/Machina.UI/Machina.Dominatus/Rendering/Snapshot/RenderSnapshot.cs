namespace Machina.Dominatus.Rendering.Snapshot;

public sealed record RenderSnapshot(
    int Width,
    int Height,
    IReadOnlyList<string> Commands);
