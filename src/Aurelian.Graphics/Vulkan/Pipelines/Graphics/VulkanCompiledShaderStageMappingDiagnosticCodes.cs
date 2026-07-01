namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

public static class VulkanCompiledShaderStageMappingDiagnosticCodes
{
    public const string MissingProgram = "AGPIM2001";
    public const string MissingStages = "AGPIM2002";
    public const string UnsupportedComputeStage = "AGPIM2003";
    public const string MissingEntryPoint = "AGPIM2004";
    public const string EmptySpirv = "AGPIM2005";
    public const string InvalidSpirvByteLength = "AGPIM2006";
    public const string InvalidSpirvMagic = "AGPIM2007";
    public const string DuplicateShaderStage = "AGPIM2008";
}
