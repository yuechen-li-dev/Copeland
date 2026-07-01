namespace Aurelian.Graphics.Vulkan.Sync;

public static class VulkanFenceDiagnosticCodes
{
    public const string TimelineSemaphoreCreationFailed = "AGF1001";
    public const string TimelineSemaphoreQueryFailed = "AGF1002";
    public const string TimelineSemaphoreWaitFailed = "AGF1003";
    public const string InvalidFenceValue = "AGF1004";
    public const string FenceDisposed = "AGF1005";
}
