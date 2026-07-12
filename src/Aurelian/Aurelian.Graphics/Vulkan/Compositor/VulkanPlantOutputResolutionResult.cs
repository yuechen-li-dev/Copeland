namespace Aurelian.Graphics.Vulkan.Compositor;

public sealed record VulkanPlantOutputResolutionResult(
    VulkanPlantOutputStatus Status,
    VulkanPlantOutputImage? Output,
    IReadOnlyList<VulkanPlantOutputDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanPlantOutputStatus.Resolved && Output is not null;
}
