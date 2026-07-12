using System.Collections.Generic;

namespace Aurelian.Rendering.Contracts.Compositor;

public sealed record CompositorDispatchRequest(
    ulong FrameId,
    CompositorPolicyKind Policy,
    IReadOnlyList<PlantOutputRef> Inputs,
    PresentationTargetRef Target)
{
    public bool HasInputs => Inputs.Count > 0;
}
