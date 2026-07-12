using Aurelian.Core.Compositor;

namespace Aurelian.Core.Engine.Graphics;

public sealed record AurelianPreparedGraphicsSubsystem(
    AurelianEngineGraphicsOptions Options,
    ICompositorMechanism? CompositorMechanism,
    IPresentationMechanism? PresentationMechanism = null);
