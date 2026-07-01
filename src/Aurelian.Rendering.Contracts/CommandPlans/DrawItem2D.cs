using Aurelian.Rendering.Contracts.Snapshots;

namespace Aurelian.Rendering.Contracts.CommandPlans;

public sealed record DrawItem2D(
    string Id,
    RenderMeshRef Mesh,
    RenderMaterialRef Material,
    RenderTransform2 Transform,
    int SortOrder = 0);
