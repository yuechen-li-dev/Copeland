using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Rendering.Contracts.Presentation;

namespace Aurelian.Core.Engine.Graphics;

public sealed record AurelianPreparedGraphicsSubsystem(
    AurelianEngineGraphicsOptions Options,
    ICompositorMechanism? CompositorMechanism,
    IPresentationMechanism? PresentationMechanism = null);
