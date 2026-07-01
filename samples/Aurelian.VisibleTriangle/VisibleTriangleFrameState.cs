using Aurelian.Core.Engine.Frames;
using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.VisibleTriangle;

internal sealed record VisibleTriangleFrameState(
    AurelianFrameId FrameId,
    uint SwapchainImageIndex,
    PlantOutputRef PlantOutput,
    PresentationTargetRef PresentationTarget);
