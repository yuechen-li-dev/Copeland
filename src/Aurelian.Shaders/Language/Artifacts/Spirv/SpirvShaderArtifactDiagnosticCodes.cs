namespace Aurelian.Shaders.Language.Artifacts.Spirv;

public static class SpirvShaderArtifactDiagnosticCodes
{
    public const string EmptyHlslSource = "ASSV1001";
    public const string MissingEntryPoint = "ASSV1002";
    public const string MissingProfile = "ASSV1003";
    public const string StageProfileMismatch = "ASSV1004";
    public const string DxcUnavailable = "ASSV1005";
    public const string DxcCompilationFailed = "ASSV1006";
    public const string EmptySpirvOutput = "ASSV1007";
    public const string DuplicateStage = "ASSV1008";
}
