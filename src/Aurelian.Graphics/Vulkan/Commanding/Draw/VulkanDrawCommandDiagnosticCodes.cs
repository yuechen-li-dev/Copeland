namespace Aurelian.Graphics.Vulkan.Commanding.Draw;

public static class VulkanDrawCommandDiagnosticCodes
{
    public const string CommandBufferNotRecording = "AGD1001";
    public const string NoActiveRenderPass = "AGD1002";
    public const string InvalidRenderPassScope = "AGD1003";
    public const string PipelineMissing = "AGD1004";
    public const string PipelineDisposed = "AGD1005";
    public const string VertexBufferMissing = "AGD1006";
    public const string VertexBufferDisposed = "AGD1007";
    public const string VertexBufferMissingVertexUsage = "AGD1008";
    public const string PlantMismatch = "AGD1009";
    public const string InvalidVertexCount = "AGD1010";
    public const string InvalidViewport = "AGD1011";
    public const string DrawRecordingFailed = "AGD1012";
}
