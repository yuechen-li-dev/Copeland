using Aurelian.Runtime.Compositor;

namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameInput(
    AurelianFrameId FrameId,
    CompositorPolicyFacts CompositorFacts,
    AurelianHostLifecycleInput? HostLifecycle = null)
{
    public AurelianHostLifecycleInput EffectiveHostLifecycle => HostLifecycle ?? AurelianHostLifecycleInput.None;
}
