using Aurelian.Runtime.Compositor;

namespace Aurelian.Core.Engine.Frames;

public sealed record AurelianFrameInput(
    AurelianFrameId FrameId,
    CompositorPolicyFacts CompositorFacts);
