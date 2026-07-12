namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public static class VulkanRenderPassCommandDiagnosticCodes
{
    public const string CommandBufferNotRecording = "AGRP1001";
    public const string RenderPassMissing = "AGRP1002";
    public const string FramebufferMissing = "AGRP1003";
    public const string RenderPassDisposed = "AGRP1004";
    public const string FramebufferDisposed = "AGRP1005";
    public const string PlantMismatch = "AGRP1006";
    public const string RenderPassFramebufferMismatch = "AGRP1007";
    public const string BeginRenderPassFailed = "AGRP1008";
    public const string EndRenderPassFailed = "AGRP1009";
    public const string RenderPassAlreadyActive = "AGRP1010";
    public const string NoActiveRenderPass = "AGRP1011";
    public const string InvalidRenderPassScope = "AGRP1012";
}
