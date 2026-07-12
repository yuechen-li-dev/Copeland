namespace Aurelian.Assets.Shaders;

public static class ShaderAssetManifestLoader
{
    public static ShaderAssetManifestLoadResult LoadShadersFromManifest(string manifestPath)
    {
        var diagnostics = new List<AssetDiagnostic>();
        var loadedShaders = new List<LoadedShaderAsset>();

        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            diagnostics.Add(new AssetDiagnostic(AssetDiagnosticCodes.TomlParseFailed, "error", "Asset manifest was not found.", manifestPath));
            return new ShaderAssetManifestLoadResult(loadedShaders, diagnostics);
        }

        string manifestText;
        try
        {
            manifestText = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(new AssetDiagnostic(AssetDiagnosticCodes.TomlParseFailed, "error", ex.Message, manifestPath));
            return new ShaderAssetManifestLoadResult(loadedShaders, diagnostics);
        }

        var (manifest, parseDiagnostics) = AssetManifestParser.Parse(manifestText, manifestPath);
        diagnostics.AddRange(parseDiagnostics);
        if (manifest is null)
        {
            return new ShaderAssetManifestLoadResult(loadedShaders, diagnostics);
        }

        diagnostics.AddRange(AssetManifestValidator.ValidateShaderReferences(manifest.ShaderReferences, manifestPath));
        if (diagnostics.Any(diagnostic => diagnostic.Fatal && string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase)))
        {
            return new ShaderAssetManifestLoadResult(loadedShaders, diagnostics);
        }

        string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        foreach (AssetShaderReference shader in manifest.ShaderReferences)
        {
            string shaderManifestPath = Path.GetFullPath(Path.Combine(manifestDirectory, shader.Path));
            if (!File.Exists(shaderManifestPath))
            {
                diagnostics.Add(new AssetDiagnostic(AssetDiagnosticCodes.ShaderArtifactMissing, "error", $"Shader asset '{shader.Id}' artifact manifest was not found.", shaderManifestPath));
                continue;
            }

            ShaderArtifactLoadResult shaderResult = ShaderArtifactLoader.LoadCompiledShaderProgram(shaderManifestPath);
            if (shaderResult.Program is not null && shaderResult.Status == ShaderArtifactLoadStatus.Loaded)
            {
                loadedShaders.Add(new LoadedShaderAsset(shader.Id, shaderManifestPath, shaderResult.Program, []));
                continue;
            }

            foreach (ShaderArtifactDiagnostic shaderDiagnostic in shaderResult.Diagnostics)
            {
                diagnostics.Add(new AssetDiagnostic(
                    AssetDiagnosticCodes.ShaderArtifactLoadFailed,
                    "error",
                    $"Shader asset '{shader.Id}' artifact load failed: {shaderDiagnostic.Code}: {shaderDiagnostic.Message}",
                    string.IsNullOrWhiteSpace(shaderDiagnostic.Path) ? shaderManifestPath : shaderDiagnostic.Path));
            }

            if (shaderResult.Diagnostics.Count == 0)
            {
                diagnostics.Add(new AssetDiagnostic(AssetDiagnosticCodes.ShaderArtifactLoadFailed, "error", $"Shader asset '{shader.Id}' artifact load failed with status '{shaderResult.Status}'.", shaderManifestPath));
            }
        }

        return new ShaderAssetManifestLoadResult(loadedShaders, diagnostics);
    }
}
