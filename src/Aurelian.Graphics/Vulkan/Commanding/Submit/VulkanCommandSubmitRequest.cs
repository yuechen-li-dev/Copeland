namespace Aurelian.Graphics.Vulkan.Commanding.Submit;

public sealed record VulkanCommandSubmitRequest(
    VulkanCommandBufferLease CommandBuffer,
    bool WaitForCompletion = true,
    ulong TimeoutNanoseconds = 5_000_000_000UL,
    string DebugName = "");
