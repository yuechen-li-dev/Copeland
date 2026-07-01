using Aurelian.Graphics.Vulkan.Device;

namespace Aurelian.Graphics.Vulkan.Diagnostics;

public sealed record VulkanInitResult(
    VulkanInitStatus Status,
    AurelianVulkanPlant? Plant,
    VulkanPlantFacts? Facts,
    IReadOnlyList<VulkanInitDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanInitStatus.Created && Plant is not null;
}
