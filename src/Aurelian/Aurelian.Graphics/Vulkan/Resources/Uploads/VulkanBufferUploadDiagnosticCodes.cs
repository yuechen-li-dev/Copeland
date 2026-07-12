namespace Aurelian.Graphics.Vulkan.Resources.Uploads;

public static class VulkanBufferUploadDiagnosticCodes
{
    public const string EmptyUpload = "AGU1001";
    public const string DestinationMissingTransferDestinationUsage = "AGU1002";
    public const string UploadOutOfBounds = "AGU1003";
    public const string PlantMismatch = "AGU1004";
    public const string StagingBufferCreationFailed = "AGU1005";
    public const string StagingBufferWriteFailed = "AGU1006";
    public const string CommandBufferBeginFailed = "AGU1007";
    public const string CommandBufferEndFailed = "AGU1008";
    public const string QueueSubmitFailed = "AGU1009";
    public const string FenceSignalValueUnavailable = "AGU1010";
    public const string UploaderDisposed = "AGU1011";
}
