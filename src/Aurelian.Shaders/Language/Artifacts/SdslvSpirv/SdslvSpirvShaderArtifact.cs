using Aurelian.Shaders.Language.Artifacts.Spirv;

namespace Aurelian.Shaders.Language.Artifacts.SdslvSpirv;

public sealed record SdslvSpirvShaderArtifact(
    string FormatVersion,
    string Language,
    string SourceName,
    string SourceSha256,
    string Hlsl,
    SpirvShaderArtifact? SpirvArtifact,
    IReadOnlyList<SdslvSpirvShaderArtifactDiagnostic> Diagnostics)
{
    public const string CurrentFormatVersion = "aurelian.sdslv.spirv.shader-artifact/0";
    public const string LanguageName = "SDSL-V";

    public bool Success => SpirvArtifact is not null
        && SpirvArtifact.Success
        && Diagnostics.All(x => x.Severity != SdslvSpirvShaderArtifactDiagnosticSeverity.Error);
}
