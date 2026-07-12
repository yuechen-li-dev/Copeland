namespace Aurelian.Rendering.Null;

public sealed record NullRenderDrawTrace(
    string Id,
    string Mesh,
    string Material,
    double X,
    double Y,
    int SortOrder);
