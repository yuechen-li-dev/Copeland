namespace Aurelian.Graphics.Vulkan.Commanding;

public static class VulkanCommandBufferDiagnosticCodes
{
    public const string CommandPoolCreationFailed = "AGC1001";
    public const string CommandBufferAllocationFailed = "AGC1002";
    public const string CommandBufferResetFailed = "AGC1003";
    public const string CommandBufferBeginFailed = "AGC1004";
    public const string CommandBufferEndFailed = "AGC1005";
    public const string CommandBufferDisposed = "AGC1006";
    public const string InvalidCommandBufferState = "AGC1007";
}
