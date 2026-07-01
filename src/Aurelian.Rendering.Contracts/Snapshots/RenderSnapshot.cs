using System.Collections.Generic;
using Aurelian.Rendering.Contracts;

namespace Aurelian.Rendering.Contracts.Snapshots;

public sealed record RenderSnapshot(
    RenderFrameId FrameId,
    IReadOnlyList<RenderCamera2D> Cameras,
    IReadOnlyList<RenderItem2D> Items)
{
    public bool IsEmpty => Cameras.Count == 0 && Items.Count == 0;
}
