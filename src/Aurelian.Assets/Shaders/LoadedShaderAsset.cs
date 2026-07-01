using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Assets.Shaders;

public sealed record LoadedShaderAsset(
    string Id,
    string ManifestPath,
    CompiledShaderProgram Program,
    IReadOnlyList<AssetDiagnostic> Diagnostics);
