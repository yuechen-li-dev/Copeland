namespace Aurelian.Shaders.Language.Artifacts;

public sealed record SdslvShaderArtifactManifest(
    string FormatVersion,
    string Language,
    string SourceName,
    SdslvShaderSourceHash SourceHash,
    IReadOnlyList<SdslvShaderArtifactStage> Stages,
    IReadOnlyList<SdslvShaderArtifactManifestDiagnostic> Diagnostics,
    bool Success,
    string Hlsl);

public sealed record SdslvShaderArtifactManifestDiagnostic(
    string Code,
    string Severity,
    string Phase,
    string Message,
    SdslvShaderArtifactManifestSpan Span);

public sealed record SdslvShaderArtifactManifestSpan(
    int Start,
    int End,
    int Line,
    int Column);
