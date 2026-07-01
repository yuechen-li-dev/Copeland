using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Runtime.Compositor;

public sealed record CompositorPolicyFacts(
    CompositorFrameFacts FrameFacts,
    RequiredPlantOutputSet RequiredOutputs,
    PresentationTargetRef Target,
    CompositorPolicyKind RequestedPolicy = CompositorPolicyKind.Passthrough);
