using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class AnalyticShape2DShaderM4Tests
{
    [Fact]
    public void AnalyticShape2D_Compiles_Through_VdMir_Hlsl_And_Spirv_Deterministically()
    {
        VdMirGraphicsModule firstModule = Compile();
        VdMirGraphicsModule secondModule = Compile();
        VdMirGraphicsBackendResult first = VdMirGraphicsBackend.Compile(firstModule);
        VdMirGraphicsBackendResult second = VdMirGraphicsBackend.Compile(secondModule);

        Assert.True(first.Vertex.SpirvValidated, first.Vertex.DxcOutput + first.Vertex.SpirvValidationOutput);
        Assert.True(first.Pixel.SpirvValidated, first.Pixel.DxcOutput + first.Pixel.SpirvValidationOutput);
        Assert.Equal(VdMirJson.Serialize(firstModule), VdMirJson.Serialize(secondModule));
        Assert.Equal(first.HlslSha256, second.HlslSha256);
        Assert.Equal(first.Vertex.SpirvSha256, second.Vertex.SpirvSha256);
        Assert.Equal(first.Pixel.SpirvSha256, second.Pixel.SpirvSha256);
        Assert.Contains("SignedDistanceRoundedRect", first.Hlsl, StringComparison.Ordinal);
        Assert.Contains("SignedDistanceCircle", first.Hlsl, StringComparison.Ordinal);
        Assert.DoesNotContain("Texture2D", first.Hlsl, StringComparison.Ordinal);
    }

    private static VdMirGraphicsModule Compile()
    {
        const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts";
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), sourceName.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        Assert.True(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        return module;
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
