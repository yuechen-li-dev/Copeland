namespace Aurelian.Shaders.Language.Artifacts.Spirv;

public sealed record SpirvShaderArtifact(
    string FormatVersion,
    string Language,
    IReadOnlyList<SpirvShaderStageArtifact> Stages,
    IReadOnlyList<SpirvShaderArtifactDiagnostic> Diagnostics)
{
    public const string CurrentFormatVersion = "aurelian.spirv.shader-artifact/0";
    public const string LanguageName = "HLSL";

    public bool Success => Diagnostics.All(x => x.Severity != SpirvShaderArtifactDiagnosticSeverity.Error) && Stages.Count > 0;
}
