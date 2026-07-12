namespace Aurelian.Shaders.Language.Artifacts.Compiled;

public static class CompiledShaderProgramExportDiagnosticCodes
{
    public const string MissingStages = "ACSH1001";
    public const string DuplicateStage = "ACSH1002";
    public const string MissingEntryPoint = "ACSH1003";
    public const string EmptySpirv = "ACSH1004";
    public const string InvalidSpirvHash = "ACSH1005";
    public const string ArtifactFailed = "ACSH1006";
    public const string MissingArtifact = "ACSH1007";
}
