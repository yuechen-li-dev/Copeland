using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class MsdfTextShaderM2Tests
{
    [Fact]
    public void MsdfText_Compiles_Through_VdMir_Hlsl_And_Spirv_Deterministically()
    {
        VdMirGraphicsModule module = Compile();
        VdMirGraphicsBackendResult first = VdMirGraphicsBackend.Compile(module);
        VdMirGraphicsBackendResult second = VdMirGraphicsBackend.Compile(Compile());

        Assert.True(first.Vertex.SpirvValidated, first.Vertex.DxcOutput + Environment.NewLine + first.Vertex.SpirvValidationOutput);
        Assert.True(first.Pixel.SpirvValidated, first.Pixel.DxcOutput + Environment.NewLine + first.Pixel.SpirvValidationOutput);
        Assert.Equal(first.HlslSha256, second.HlslSha256);
        Assert.Equal(first.Vertex.SpirvSha256, second.Vertex.SpirvSha256);
        Assert.Equal(first.Pixel.SpirvSha256, second.Pixel.SpirvSha256);
        Assert.Contains("Median3", first.Hlsl, StringComparison.Ordinal);
        Assert.Contains("atlas.Sample(linearSampler, input.uv)", first.Hlsl, StringComparison.Ordinal);
        Assert.Contains("float pixelRange; // offset 16", first.Hlsl, StringComparison.Ordinal);
        Assert.Contains("float threshold; // offset 20", first.Hlsl, StringComparison.Ordinal);
        Assert.Contains("float fieldScale : TEXCOORD1", first.Hlsl, StringComparison.Ordinal);
    }

    internal static VdMirGraphicsModule Compile()
    {
        const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts";
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), sourceName.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        Assert.True(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        return module;
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
