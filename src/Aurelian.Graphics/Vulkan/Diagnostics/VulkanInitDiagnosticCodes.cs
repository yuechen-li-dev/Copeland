namespace Aurelian.Graphics.Vulkan.Diagnostics;

public static class VulkanInitDiagnosticCodes
{
    public const string VulkanLoaderUnavailable = "AGV1001";
    public const string InstanceCreationFailed = "AGV1002";
    public const string ValidationLayerUnavailable = "AGV1003";
    public const string NoPhysicalDevices = "AGV1004";
    public const string NoSuitableQueueFamily = "AGV1005";
    public const string RequiredExtensionMissing = "AGV1006";
    public const string TimelineSemaphoreUnsupported = "AGV1007";
    public const string DeviceCreationFailed = "AGV1008";
    public const string DebugUtilsUnavailable = "AGV1009";
    public const string DeviceSelected = "AGV1010";
    public const string PresentationSurfaceExtensionsUnavailable = "AGV1011";
}
