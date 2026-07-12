using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;

namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameCompositorInputs(
    CompositorFrameFacts FrameFacts,
    RequiredPlantOutputSet RequiredOutputs,
    PresentationTargetRef Target,
    CompositorPolicyKind RequestedPolicy = CompositorPolicyKind.Passthrough)
{
    public CompositorPolicyFacts ToPolicyFacts() => new(
        FrameFacts,
        RequiredOutputs,
        Target,
        RequestedPolicy);
}
