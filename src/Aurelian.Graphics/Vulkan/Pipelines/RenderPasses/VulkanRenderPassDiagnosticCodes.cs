namespace Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

public static class VulkanRenderPassDiagnosticCodes
{
    public const string NoColorAttachments = "AGR1001";
    public const string MultipleColorAttachmentsUnsupported = "AGR1002";
    public const string UnsupportedAttachmentFormat = "AGR1003";
    public const string UnsupportedInitialLayout = "AGR1004";
    public const string UnsupportedFinalLayout = "AGR1005";
    public const string RenderPassCreationFailed = "AGR1006";
    public const string RenderPassDisposed = "AGR1007";
    public const string PlantMismatch = "AGR1008";
}
