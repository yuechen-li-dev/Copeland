namespace Aurelian.Assets.Shaders;

public sealed record ShaderAssetManifestLoadResult(
    IReadOnlyList<LoadedShaderAsset> Shaders,
    IReadOnlyList<AssetDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(diagnostic => !string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase));
}
