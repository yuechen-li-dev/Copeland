namespace Aurelian.Graphics.Vulkan.Sync;

public sealed record VulkanFenceOperationResult(
    VulkanFenceStatus Status,
    ulong? Value,
    IReadOnlyList<VulkanFenceDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanFenceStatus.Succeeded;

    public static VulkanFenceOperationResult Succeeded(ulong value)
        => new(VulkanFenceStatus.Succeeded, value, []);

    public static VulkanFenceOperationResult Failed(ulong? value, VulkanFenceDiagnostic diagnostic)
        => new(VulkanFenceStatus.Failed, value, [diagnostic]);
}
