namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public static class VulkanCompiledGraphicsPipelineDiagnosticCodes
{
    public const string ProgramMissing = "AGCP1001";
    public const string MissingVertexStage = "AGCP1002";
    public const string MissingFragmentStage = "AGCP1003";
    public const string DuplicateStage = "AGCP1004";
    public const string UnsupportedComputeStage = "AGCP1005";
    public const string InvalidSpirvBytes = "AGCP1006";
    public const string InvalidVertexInput = "AGCP1007";
    public const string DescriptorCreationFailed = "AGCP1008";
    public const string NativePipelineCreationFailed = "AGCP1009";
}
