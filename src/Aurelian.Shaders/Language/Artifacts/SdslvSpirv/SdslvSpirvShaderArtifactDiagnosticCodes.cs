namespace Aurelian.Shaders.Language.Artifacts.SdslvSpirv;

public static class SdslvSpirvShaderArtifactDiagnosticCodes
{
    public const string ParseFailed = "ASV1001";
    public const string ValidationFailed = "ASV1002";
    public const string HlslEmissionFailed = "ASV1003";
    public const string StageExtractionFailed = "ASV1004";
    public const string SpirvCompilationUnavailable = "ASV1005";
    public const string SpirvCompilationFailed = "ASV1006";
    public const string MissingVertexStage = "ASV1007";
    public const string MissingFragmentStage = "ASV1008";
    public const string UnsupportedStageShape = "ASV1009";
}
