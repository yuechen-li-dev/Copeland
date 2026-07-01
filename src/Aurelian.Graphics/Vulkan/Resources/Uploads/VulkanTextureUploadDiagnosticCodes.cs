namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public static class VulkanTextureUploadDiagnosticCodes
{
    public const string EmptyUpload = "AGTU1001";
    public const string DestinationMissingTransferDestinationUsage = "AGTU1002";
    public const string UnsupportedTextureFormat = "AGTU1003";
    public const string UploadSizeMismatch = "AGTU1004";
    public const string PlantMismatch = "AGTU1005";
    public const string StagingBufferCreationFailed = "AGTU1006";
    public const string StagingBufferWriteFailed = "AGTU1007";
    public const string CommandBufferBeginFailed = "AGTU1008";
    public const string BarrierEmissionFailed = "AGTU1009";
    public const string CopyBufferToImageFailed = "AGTU1010";
    public const string CommandBufferEndFailed = "AGTU1011";
    public const string QueueSubmitFailed = "AGTU1012";
    public const string FenceSignalValueUnavailable = "AGTU1013";
    public const string UploaderDisposed = "AGTU1014";
    public const string DestinationTextureDisposed = "AGTU1015";
}
