namespace Aurelian.Shaders.Language.Artifacts.SdslvSpirv;

public sealed record SdslvSpirvShaderArtifactDiagnostic(
    string Code,
    SdslvSpirvShaderArtifactDiagnosticSeverity Severity,
    string Message);
