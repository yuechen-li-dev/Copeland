namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public static class VulkanGraphicsPipelineDiagnosticCodes
{
    public const string MissingVertexShader = "AGPIP1001";
    public const string MissingFragmentShader = "AGPIP1002";
    public const string DuplicateShaderStage = "AGPIP1003";
    public const string InvalidEntryPoint = "AGPIP1004";
    public const string EmptySpirv = "AGPIP1005";
    public const string InvalidVertexInput = "AGPIP1006";
    public const string UnsupportedDepthState = "AGPIP1007";
    public const string RenderPassMissing = "AGPIP1008";
    public const string RenderPassDisposed = "AGPIP1009";
    public const string ShaderModuleCreationFailed = "AGPIP1010";
    public const string PipelineLayoutCreationFailed = "AGPIP1011";
    public const string GraphicsPipelineCreationFailed = "AGPIP1012";
    public const string PipelineDisposed = "AGPIP1013";
    public const string PlantMismatch = "AGPIP1014";
}
