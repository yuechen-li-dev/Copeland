using System.Security.Cryptography;
using Aurelian.Assets.Shaders;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class AssetManifestShaderReferencesM0Tests
{
    [Fact]
    public void AssetManifestParser_ParsesShaderReferences()
    {
        const string toml = """
            [[shaders]]
            id = "smoke_triangle"
            path = "Shaders/SmokeTriangle/shader.toml"
            """;

        var (manifest, diagnostics) = AssetManifestParser.Parse(toml, "assets.toml");

        Assert.Empty(diagnostics);
        AssetShaderReference shader = Assert.Single(manifest!.ShaderReferences);
        Assert.Equal("smoke_triangle", shader.Id);
        Assert.Equal("Shaders/SmokeTriangle/shader.toml", shader.Path);
    }

    [Fact]
    public void AssetManifestParser_AbsentShaders_UsesEmptyList()
    {
        var (manifest, diagnostics) = AssetManifestParser.Parse(string.Empty, "assets.toml");

        Assert.Empty(diagnostics);
        Assert.Empty(manifest!.ShaderReferences);
    }

    [Fact]
    public void AssetManifestValidator_RejectsMissingShaderId()
    {
        var manifest = new AssetManifest();
        manifest.ShaderReferences.Add(new AssetShaderReference(string.Empty, "Shaders/SmokeTriangle/shader.toml"));

        IReadOnlyList<AssetDiagnostic> diagnostics = AssetManifestValidator.Validate(manifest, "assets.toml");

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == AssetDiagnosticCodes.ShaderIdMissing);
    }

    [Fact]
    public void AssetManifestValidator_RejectsMissingShaderPath()
    {
        var manifest = new AssetManifest();
        manifest.ShaderReferences.Add(new AssetShaderReference("smoke_triangle", string.Empty));

        IReadOnlyList<AssetDiagnostic> diagnostics = AssetManifestValidator.Validate(manifest, "assets.toml");

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == AssetDiagnosticCodes.ShaderPathMissing);
    }

    [Fact]
    public void AssetManifestValidator_RejectsDuplicateShaderIds()
    {
        var manifest = new AssetManifest();
        manifest.ShaderReferences.Add(new AssetShaderReference("smoke_triangle", "Shaders/A/shader.toml"));
        manifest.ShaderReferences.Add(new AssetShaderReference("smoke_triangle", "Shaders/B/shader.toml"));

        IReadOnlyList<AssetDiagnostic> diagnostics = AssetManifestValidator.Validate(manifest, "assets.toml");

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == AssetDiagnosticCodes.DuplicateShaderId);
    }

    [Fact]
    public void AssetManifestValidator_RejectsAbsoluteShaderPath()
    {
        var manifest = new AssetManifest();
        manifest.ShaderReferences.Add(new AssetShaderReference("smoke_triangle", Path.Combine(Path.GetTempPath(), "shader.toml")));

        IReadOnlyList<AssetDiagnostic> diagnostics = AssetManifestValidator.Validate(manifest, "assets.toml");

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == AssetDiagnosticCodes.ShaderPathAbsoluteUnsupported);
    }

    [Fact]
    public void AssetManifestValidator_RejectsPathTraversal()
    {
        var manifest = new AssetManifest();
        manifest.ShaderReferences.Add(new AssetShaderReference("smoke_triangle", "Shaders/../shader.toml"));

        IReadOnlyList<AssetDiagnostic> diagnostics = AssetManifestValidator.Validate(manifest, "assets.toml");

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == AssetDiagnosticCodes.ShaderPathTraversalUnsupported);
    }

    [Fact]
    public void ShaderAssetManifestLoader_LoadsShaderArtifactFromManifestRelativePath()
    {
        using var temp = TempDirectory.Create();
        WriteManifestAndShaderArtifact(temp.Path);

        ShaderAssetManifestLoadResult result = ShaderAssetManifestLoader.LoadShadersFromManifest(Path.Combine(temp.Path, "assets.toml"));

        Assert.True(result.Success, FormatDiagnostics(result));
        LoadedShaderAsset shader = Assert.Single(result.Shaders);
        Assert.Equal("smoke_triangle", shader.Id);
        Assert.Equal(Path.GetFullPath(Path.Combine(temp.Path, "Shaders", "SmokeTriangle", "shader.toml")), shader.ManifestPath);
        Assert.Equal(2, shader.Program.Stages.Count);
    }

    [Fact]
    public void ShaderAssetManifestLoader_MapsShaderArtifactLoadFailureDiagnostics()
    {
        using var temp = TempDirectory.Create();
        WriteManifestAndShaderArtifact(temp.Path);
        File.Delete(Path.Combine(temp.Path, "Shaders", "SmokeTriangle", "VSMain.spv.hex"));

        ShaderAssetManifestLoadResult result = ShaderAssetManifestLoader.LoadShadersFromManifest(Path.Combine(temp.Path, "assets.toml"));

        Assert.False(result.Success);
        Assert.Empty(result.Shaders);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == AssetDiagnosticCodes.ShaderArtifactLoadFailed);
    }

    [Fact]
    public void ShaderAssetManifestLoader_DoesNotRequireGraphicsRuntime()
    {
        using var temp = TempDirectory.Create();
        WriteManifestAndShaderArtifact(temp.Path);

        ShaderAssetManifestLoadResult result = ShaderAssetManifestLoader.LoadShadersFromManifest(Path.Combine(temp.Path, "assets.toml"));

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.DoesNotContain(typeof(ShaderAssetManifestLoader).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "Aurelian.Graphics");
    }

    private static void WriteManifestAndShaderArtifact(string root)
    {
        string shaderDirectory = Path.Combine(root, "Shaders", "SmokeTriangle");
        Directory.CreateDirectory(shaderDirectory);
        File.WriteAllText(Path.Combine(root, "assets.toml"), """
            [[shaders]]
            id = "smoke_triangle"
            path = "Shaders/SmokeTriangle/shader.toml"
            """);
        WriteArtifact(shaderDirectory);
    }

    private static void WriteArtifact(string directory)
    {
        byte[] vertex = [0x03, 0x02, 0x23, 0x07, 0x01, 0x00, 0x00, 0x00];
        byte[] fragment = [0x03, 0x02, 0x23, 0x07, 0x02, 0x00, 0x00, 0x00];
        File.WriteAllText(Path.Combine(directory, "VSMain.spv.hex"), EncodeHex(vertex));
        File.WriteAllText(Path.Combine(directory, "PSMain.spv.hex"), EncodeHex(fragment));
        File.WriteAllText(Path.Combine(directory, "shader.toml"), $$"""
            format = "{{ShaderArtifactManifest.CurrentFormatVersion}}"
            source_language = "SDSL-V"
            source_name = "smoke_triangle.sdslv"
            source_sha256 = "{{new string('1', 64)}}"

            [[stages]]
            stage = "vertex"
            entry_point = "VSMain"
            profile = "vs_6_0"
            spirv_encoding = "hex"
            spirv_path = "VSMain.spv.hex"
            spirv_sha256 = "{{ComputeSha256(vertex)}}"
            source_name = "smoke_triangle.sdslv"

            [[stages]]
            stage = "fragment"
            entry_point = "PSMain"
            profile = "ps_6_0"
            spirv_encoding = "hex"
            spirv_path = "PSMain.spv.hex"
            spirv_sha256 = "{{ComputeSha256(fragment)}}"
            source_name = "smoke_triangle.sdslv"
            """);
    }

    private static string EncodeHex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2"))) + Environment.NewLine;

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatDiagnostics(ShaderAssetManifestLoadResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;
        public string Path { get; }
        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
