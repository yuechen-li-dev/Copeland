namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed record VulkanBufferUploadResult(
    VulkanBufferUploadStatus Status,
    ulong? SignalFenceValue,
    IReadOnlyList<VulkanBufferUploadDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanBufferUploadStatus.Submitted && SignalFenceValue is not null;
}
