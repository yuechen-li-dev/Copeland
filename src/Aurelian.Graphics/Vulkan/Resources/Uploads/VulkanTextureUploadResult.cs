namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public sealed record VulkanTextureUploadResult(
    VulkanTextureUploadStatus Status,
    ulong? SignalFenceValue,
    IReadOnlyList<VulkanTextureUploadDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanTextureUploadStatus.Submitted && SignalFenceValue is not null;
}
