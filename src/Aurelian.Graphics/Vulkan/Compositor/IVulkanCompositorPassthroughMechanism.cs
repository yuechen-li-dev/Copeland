using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Compositor;

public interface IVulkanCompositorPassthroughMechanism
{
    VulkanCompositorResult Dispatch(
        CompositorDispatchRequest request,
        VulkanPlantOutputImageSet plantOutputs,
        VulkanPresentationTargetImageSet presentationTargets);
}
