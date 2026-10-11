using Aurelian.Games;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class BuiltinGameShaderTests
{
    [Fact]
    public void GameAssetsCompileAndCacheEngineSourcesWithoutEmbeddingTheirOwnShaderCopies()
    {
        var assets = new GameAssets();
        var graphics = assets.Shader("SurfaceResolve3D.v.ts");
        Assert.Same(graphics, assets.Shader("SurfaceResolve3D.v.ts"));
        byte[] rayQuery = assets.ComputeShader("RayQuery.v.ts");
        Assert.NotEmpty(rayQuery);
        Assert.Same(rayQuery, assets.ComputeShader("RayQuery.v.ts"));
        Assert.DoesNotContain(typeof(GameAssets).Assembly.GetManifestResourceNames(),
            name => name.EndsWith(".v.ts", StringComparison.Ordinal));
    }

    [Fact]
    public void GameAssetRequestsRejectSharedModulesAndMismatchedPipelineKindsBeforeCompilation()
    {
        var assets = new GameAssets();
        Assert.StartsWith("AUR-SHADER-LIBRARY-003:", Assert.Throws<ArgumentException>(() => assets.Shader("Lighting3D.v.ts")).Message);
        Assert.StartsWith("AUR-SHADER-LIBRARY-003:", Assert.Throws<ArgumentException>(() => assets.ComputeShader("ToneMap3D.v.ts")).Message);
    }
}
