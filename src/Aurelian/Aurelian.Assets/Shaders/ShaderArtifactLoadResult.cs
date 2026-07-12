using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Assets.Shaders;

public sealed record ShaderArtifactLoadResult(
    ShaderArtifactLoadStatus Status,
    CompiledShaderProgram? Program,
    IReadOnlyList<ShaderArtifactDiagnostic> Diagnostics)
{
    public bool Success => Status == ShaderArtifactLoadStatus.Loaded && Program is not null;
}
