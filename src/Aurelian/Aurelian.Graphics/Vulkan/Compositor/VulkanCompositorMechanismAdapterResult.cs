using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Compositor;

public sealed record VulkanCompositorMechanismAdapterResult(
    CompositorDispatchResult DispatchResult,
    IReadOnlyList<VulkanCompositorMechanismAdapterDiagnostic> Diagnostics)
{
    public bool Success => DispatchResult.Success && Diagnostics.Count == 0;
}
