namespace Aurelian.Graphics.Vulkan.Presentation;

public static class VulkanPresentationDiagnosticCodes
{
    public const string WindowCreationUnavailable = "AGPR1001";
    public const string SurfaceCreationFailed = "AGPR1002";
    public const string SwapchainExtensionMissing = "AGPR1003";
    public const string SurfaceSupportMissing = "AGPR1004";
    public const string NoSurfaceFormats = "AGPR1005";
    public const string NoPresentModes = "AGPR1006";
    public const string SwapchainCreationFailed = "AGPR1007";
    public const string SwapchainImageQueryFailed = "AGPR1008";
    public const string ImageViewCreationFailed = "AGPR1009";
    public const string AcquireFailed = "AGPR1010";
    public const string PresentFailed = "AGPR1011";
    public const string PresentationDisposed = "AGPR1012";
    public const string HeadlessEnvironment = "AGPR1013";
    public const string AcquirePresentDeferred = "AGPR1014";
    public const string SemaphoreCreationFailed = "AGPR1015";
    public const string InvalidImageIndex = "AGPR1016";
    public const string SwapchainOutOfDate = "AGPR1017";
    public const string SwapchainSuboptimal = "AGPR1018";
    public const string SurfaceLost = "AGPR1019";
    public const string PresentDeferredRemoved = "AGPR1020";
}
