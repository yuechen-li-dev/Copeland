using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Compositor;

public sealed record VulkanCompositorResult(
    VulkanCompositorStatus Status,
    CompositorDispatchResult DispatchResult,
    ulong? SignalFenceValue,
    IReadOnlyList<VulkanCompositorDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanCompositorStatus.Dispatched
        && DispatchResult.Success
        && SignalFenceValue is not null;
}
