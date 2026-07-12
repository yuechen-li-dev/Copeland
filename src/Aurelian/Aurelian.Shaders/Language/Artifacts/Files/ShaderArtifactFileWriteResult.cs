namespace Aurelian.Shaders.Language.Artifacts.Files;

public sealed record ShaderArtifactFileWriteResult(
    ShaderArtifactFileWriteStatus Status,
    ShaderArtifactFileSet? FileSet,
    IReadOnlyList<ShaderArtifactFileDiagnostic> Diagnostics)
{
    public bool Success => Status == ShaderArtifactFileWriteStatus.Written && FileSet is not null;
}
