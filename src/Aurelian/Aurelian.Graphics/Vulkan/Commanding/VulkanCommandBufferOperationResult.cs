namespace Aurelian.Graphics.Vulkan.Commanding;

public sealed record VulkanCommandBufferOperationResult(
    VulkanCommandBufferStatus Status,
    IReadOnlyList<VulkanCommandBufferDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanCommandBufferStatus.Succeeded;

    public static VulkanCommandBufferOperationResult Succeeded()
        => new(VulkanCommandBufferStatus.Succeeded, Array.Empty<VulkanCommandBufferDiagnostic>());

    public static VulkanCommandBufferOperationResult Failed(VulkanCommandBufferDiagnostic diagnostic)
        => new(VulkanCommandBufferStatus.Failed, [diagnostic]);
}

public sealed record VulkanCommandBufferLeaseResult(
    VulkanCommandBufferStatus Status,
    VulkanCommandBufferLease? Lease,
    IReadOnlyList<VulkanCommandBufferDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanCommandBufferStatus.Succeeded && Lease is not null;
}
