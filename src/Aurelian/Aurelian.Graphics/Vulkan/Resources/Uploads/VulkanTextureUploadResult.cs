namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

/// <summary>Opt-in synchronous upload diagnostics; GPU timestamps are not host submission times.</summary>
public sealed record VulkanTextureUploadTimings(double StagingMilliseconds, double RecordingMilliseconds,
    double QueueSubmitMilliseconds, double FenceWaitMilliseconds, double RetirementMilliseconds,
    Aurelian.Graphics.Vulkan.Native3D.Native3DCompletedGpuFrame? GpuInterval);

public sealed record VulkanTextureUploadResult(
    VulkanTextureUploadStatus Status,
    ulong? SignalFenceValue,
    IReadOnlyList<VulkanTextureUploadDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanTextureUploadStatus.Submitted && SignalFenceValue is not null;
}
