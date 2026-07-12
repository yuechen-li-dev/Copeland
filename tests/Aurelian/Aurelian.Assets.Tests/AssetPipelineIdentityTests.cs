using Aurelian.Assets;
using Xunit;

namespace Aurelian.Assets.Tests;

public sealed class AssetPipelineIdentityTests
{
    [Fact]
    public void AssemblyName_IsAurelianAssets()
    {
        var assemblyName = typeof(AssetManifest).Assembly.GetName().Name;

        Assert.Equal("Aurelian.Assets", assemblyName);
    }

    [Fact]
    public void ManifestParser_CanReadBasicShaderRecord()
    {
        const string toml = """
            [[shader]]
            id = "smoke.shader"
            source = "shaders/smoke.sdsl"
            entry = "Smoke"
            backend = "vulkan"
            profile = "default"
            """;

        var (manifest, diagnostics) = AssetManifestParser.Parse(toml, "assets.toml");

        Assert.Empty(diagnostics);
        var shader = Assert.Single(manifest!.Shaders);
        Assert.Equal("smoke.shader", shader.Id);
        Assert.Equal("shaders/smoke.sdsl", shader.Source);
        Assert.Equal("Smoke", shader.Entry);
    }

    [Fact]
    public void ManifestValidator_AcceptsBasicShaderRecord()
    {
        var manifest = new AssetManifest();
        manifest.Shaders.Add(new ShaderAssetRecord("smoke.shader", "shaders/smoke.sdsl", "Smoke", "vulkan", "default"));

        var diagnostics = AssetManifestValidator.Validate(manifest, "assets.toml");

        Assert.Empty(diagnostics);
    }
}
