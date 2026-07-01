using Aurelian.Core.Compositor;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Core.Graphics.Vulkan.Compositor;

public sealed class VulkanCompositorMechanismAdapter : ICompositorMechanism
{
    private readonly IVulkanCompositorPassthroughMechanism passthrough;
    private readonly VulkanPlantOutputImageSet plantOutputs;
    private readonly VulkanPresentationTargetImageSet presentationTargets;

    public VulkanCompositorMechanismAdapter(
        VulkanCompositorPassthrough passthrough,
        VulkanPlantOutputImageSet plantOutputs,
        VulkanPresentationTargetImageSet presentationTargets)
        : this((IVulkanCompositorPassthroughMechanism)passthrough, plantOutputs, presentationTargets)
    {
    }

    public VulkanCompositorMechanismAdapter(
        IVulkanCompositorPassthroughMechanism passthrough,
        VulkanPlantOutputImageSet plantOutputs,
        VulkanPresentationTargetImageSet presentationTargets)
    {
        ArgumentNullException.ThrowIfNull(passthrough);
        ArgumentNullException.ThrowIfNull(plantOutputs);
        ArgumentNullException.ThrowIfNull(presentationTargets);

        this.passthrough = passthrough;
        this.plantOutputs = plantOutputs;
        this.presentationTargets = presentationTargets;
    }

    public Task<CompositorDispatchResult> DispatchAsync(
        CompositorDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new CompositorDispatchResult(
                CompositorDispatchStatus.Skipped,
                request.FrameId,
                request.Policy,
                request.Target,
                CompositorDiagnostics.Empty,
                [new CompositorDispatchDiagnostic(
                    VulkanCompositorMechanismAdapterDiagnosticCodes.CancellationRequested,
                    CompositorDispatchDiagnosticSeverity.Warning,
                    "Vulkan compositor mechanism adapter dispatch was skipped because cancellation was requested.")]));
        }

        VulkanCompositorResult result = passthrough.Dispatch(request, plantOutputs, presentationTargets);
        return Task.FromResult(result.DispatchResult);
    }
}
