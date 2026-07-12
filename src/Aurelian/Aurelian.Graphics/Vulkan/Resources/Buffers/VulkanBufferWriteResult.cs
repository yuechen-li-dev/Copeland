namespace Aurelian.Graphics.Vulkan.Resources.Buffers;

public sealed record VulkanBufferWriteResult(
    VulkanBufferWriteStatus Status,
    IReadOnlyList<VulkanBufferWriteDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanBufferWriteStatus.Written;

    public static VulkanBufferWriteResult Written { get; } = new(VulkanBufferWriteStatus.Written, []);
}
