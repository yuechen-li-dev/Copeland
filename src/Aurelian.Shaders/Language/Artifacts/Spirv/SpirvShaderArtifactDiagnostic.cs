namespace Aurelian.Shaders.Language.Artifacts.Spirv;

public sealed record SpirvShaderArtifactDiagnostic(
    string Code,
    SpirvShaderArtifactDiagnosticSeverity Severity,
    string Message);
