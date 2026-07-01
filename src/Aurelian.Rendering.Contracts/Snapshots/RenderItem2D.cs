namespace Aurelian.Rendering.Contracts.Snapshots;

public sealed record RenderItem2D(
    string Id,
    RenderTransform2 Transform,
    RenderMeshRef Mesh,
    RenderMaterialRef Material,
    int SortOrder = 0);
