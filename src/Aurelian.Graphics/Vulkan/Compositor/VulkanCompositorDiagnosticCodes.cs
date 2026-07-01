namespace Aurelian.Graphics.Vulkan.Compositor;

public static class VulkanCompositorDiagnosticCodes
{
    public const string UnsupportedPolicy = "AGCOMP1001";
    public const string MissingInput = "AGCOMP1002";
    public const string MultipleInputsUnsupported = "AGCOMP1003";
    public const string PlantOutputResolutionFailed = "AGCOMP1004";
    public const string PresentationTargetResolutionFailed = "AGCOMP1005";
    public const string SizeMismatch = "AGCOMP1006";
    public const string FormatMismatch = "AGCOMP1007";
    public const string CommandBufferBeginFailed = "AGCOMP1008";
    public const string BarrierEmissionFailed = "AGCOMP1009";
    public const string CopyImageFailed = "AGCOMP1010";
    public const string CommandBufferEndFailed = "AGCOMP1011";
    public const string SubmitFailed = "AGCOMP1012";
    public const string CompositorDisposed = "AGCOMP1013";
}
