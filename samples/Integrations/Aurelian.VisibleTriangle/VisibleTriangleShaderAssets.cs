using Aurelian.Assets;
using Aurelian.Assets.Shaders;
using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.VisibleTriangle;

internal static class VisibleTriangleShaderAssets
{
    private const string ManifestRelativePath = "Assets/assets.toml";
    private const string SmokeTriangleShaderId = "smoke_triangle";

    public static CompiledShaderProgram LoadSmokeTriangleShader(TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(log);

        string manifestPath = Path.Combine(AppContext.BaseDirectory, "Assets", "assets.toml");
        log.WriteLine($"Loading visible triangle shader assets from manifest: {manifestPath}");

        ShaderAssetManifestLoadResult result = ShaderAssetManifestLoader.LoadShadersFromManifest(manifestPath);
        LoadedShaderAsset? smokeTriangle = result.Shaders.FirstOrDefault(static shader =>
            string.Equals(shader.Id, SmokeTriangleShaderId, StringComparison.Ordinal));

        if (!result.Success || smokeTriangle is null)
        {
            PrintManifestDiagnostics(log, manifestPath, result, smokeTriangle is not null);
            throw new VisibleTriangleSampleException($"Visible triangle shader asset '{SmokeTriangleShaderId}' could not be loaded from '{ManifestRelativePath}'.");
        }

        if (result.Diagnostics.Count > 0 || smokeTriangle.Diagnostics.Count > 0)
        {
            PrintManifestDiagnostics(log, manifestPath, result, smokeTriangleFound: true);
        }

        log.WriteLine($"Loaded visible triangle shader asset '{SmokeTriangleShaderId}' from artifact: {smokeTriangle.ManifestPath}");
        return smokeTriangle.Program;
    }

    private static void PrintManifestDiagnostics(
        TextWriter log,
        string manifestPath,
        ShaderAssetManifestLoadResult result,
        bool smokeTriangleFound)
    {
        log.WriteLine("Visible triangle shader asset manifest diagnostics:");
        log.WriteLine($"  Manifest path: {manifestPath}");
        log.WriteLine($"  Requested shader id: {SmokeTriangleShaderId}");
        log.WriteLine($"  Requested shader found: {smokeTriangleFound}");
        log.WriteLine($"  Loaded shader asset count: {result.Shaders.Count}");

        if (result.Diagnostics.Count == 0)
        {
            log.WriteLine("  Aggregate diagnostics: <none>");
        }
        else
        {
            log.WriteLine("  Aggregate diagnostics:");
            foreach (AssetDiagnostic diagnostic in result.Diagnostics)
            {
                log.WriteLine($"    {diagnostic.Code} [{diagnostic.Severity}] {diagnostic.SourcePath}:{diagnostic.Line}:{diagnostic.Column}: {diagnostic.Message}");
            }
        }

        if (result.Shaders.Count == 0)
        {
            log.WriteLine("  Per-shader diagnostics: <no shader assets loaded>");
            return;
        }

        log.WriteLine("  Per-shader diagnostics:");
        foreach (LoadedShaderAsset shader in result.Shaders)
        {
            log.WriteLine($"    {shader.Id}: artifact={shader.ManifestPath}");
            if (shader.Diagnostics.Count == 0)
            {
                log.WriteLine("      <none>");
                continue;
            }

            foreach (AssetDiagnostic diagnostic in shader.Diagnostics)
            {
                log.WriteLine($"      {diagnostic.Code} [{diagnostic.Severity}] {diagnostic.SourcePath}:{diagnostic.Line}:{diagnostic.Column}: {diagnostic.Message}");
            }
        }
    }
}
