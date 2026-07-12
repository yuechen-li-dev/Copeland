namespace Aurelian.Graphics.Vulkan.Commanding.Submit;

public static class VulkanCommandSubmitDiagnosticCodes
{
    public const string CommandBufferMissing = "AGCS1001";
    public const string CommandBufferNotExecutable = "AGCS1002";
    public const string PlantMismatch = "AGCS1003";
    public const string FenceSignalValueUnavailable = "AGCS1004";
    public const string QueueSubmitFailed = "AGCS1005";
    public const string FenceWaitFailed = "AGCS1006";
    public const string SubmitterDisposed = "AGCS1007";
    public const string CommandBufferRetireFailed = "AGCS1008";
}
