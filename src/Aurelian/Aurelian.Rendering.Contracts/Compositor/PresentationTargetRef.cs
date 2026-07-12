namespace Aurelian.Rendering.Contracts.Compositor;

public readonly record struct PresentationTargetRef(
    uint PlantId,
    uint SwapchainImageIndex,
    ulong FrameId)
{
    public override string ToString()
        => $"{PlantId}:{FrameId}:swapchain[{SwapchainImageIndex}]";
}
