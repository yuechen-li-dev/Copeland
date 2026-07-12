namespace Aurelian.Graphics.Vulkan.Resources.Allocation;

public sealed record VulkanAllocationResult(
    VulkanMemoryAllocatorStatus Status,
    VulkanMemoryAllocation? Allocation,
    IReadOnlyList<VulkanMemoryAllocatorDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanMemoryAllocatorStatus.Allocated && Allocation is not null;
}
