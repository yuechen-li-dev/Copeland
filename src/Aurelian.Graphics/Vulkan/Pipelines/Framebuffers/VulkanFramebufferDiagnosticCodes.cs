namespace Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;

public static class VulkanFramebufferDiagnosticCodes
{
    public const string InvalidDimensions = "AGFB1001";
    public const string NoColorAttachments = "AGFB1002";
    public const string MultipleColorAttachmentsUnsupported = "AGFB1003";
    public const string AttachmentMissing = "AGFB1004";
    public const string AttachmentDisposed = "AGFB1005";
    public const string AttachmentSizeMismatch = "AGFB1006";
    public const string AttachmentMissingColorUsage = "AGFB1007";
    public const string AttachmentMissingImageView = "AGFB1008";
    public const string PlantMismatch = "AGFB1009";
    public const string RenderPassAttachmentMismatch = "AGFB1010";
    public const string FramebufferCreationFailed = "AGFB1011";
    public const string FramebufferDisposed = "AGFB1012";
}
