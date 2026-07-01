using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.Artifacts;

public sealed record SdslvShaderArtifact(
    string FormatVersion,
    string Language,
    string SourceName,
    SdslvShaderSourceHash SourceHash,
    string Hlsl,
    IReadOnlyList<SdslvShaderArtifactStage> Stages,
    IReadOnlyList<SdslvDiagnostic> Diagnostics)
{
    public const string LanguageName = "Aurelian SDSL-V";

    public bool Success => Diagnostics.All(x => x.Severity != SdslvDiagnosticSeverity.Error);
}
