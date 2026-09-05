using System.Security.Cryptography;
using System.Text;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SoftShockwaveShaderM8Tests
{
    [Fact]
    public void SoftShockwaveCompilesThroughVdMirHlslAndSpirvDeterministically()
    {
        (string source, VdMirGraphicsModule module, VdMirGraphicsBackendResult backend) first = Compile();
        (string source, VdMirGraphicsModule module, VdMirGraphicsBackendResult backend) second = Compile();

        Assert.True(first.module.Success, Diagnostics(first.module));
        Assert.True(first.backend.Vertex.SpirvValidated, first.backend.Vertex.DxcOutput + first.backend.Vertex.SpirvValidationOutput);
        Assert.True(first.backend.Pixel.SpirvValidated, first.backend.Pixel.DxcOutput + first.backend.Pixel.SpirvValidationOutput);
        Assert.Equal(VdMirJson.Serialize(first.module), VdMirJson.Serialize(second.module));
        Assert.Equal(first.backend.HlslSha256, second.backend.HlslSha256);
        Assert.Equal(first.backend.Vertex.SpirvSha256, second.backend.Vertex.SpirvSha256);
        Assert.Equal(first.backend.Pixel.SpirvSha256, second.backend.Pixel.SpirvSha256);
        Assert.Contains("SoftRing", first.backend.Hlsl, StringComparison.Ordinal);
        Assert.Equal(Hash(first.source), Hash(second.source));
    }

    [Fact]
    public void UnsupportedManagedAllocationHasActionableGpuDiagnostic()
    {
        const string invalidSource = """
            @compute
            @numthreads(1, 1, 1)
            function Main(): void {
                const illegal = new Array<f32>();
            }
            """;
        VdMirComputeModule module = GpuComputeBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile("InvalidEffect.v.ts", invalidSource)]));

        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic =>
            diagnostic.Code == "COPE-GPU-CLOSURE-0001"
            && diagnostic.Message.Contains("managed allocation", StringComparison.OrdinalIgnoreCase));
    }

    private static (string Source, VdMirGraphicsModule Module, VdMirGraphicsBackendResult Backend) Compile()
    {
        const string sourceName = "src/Aurelian/Aurelian.Shaders/Assets/SoftShockwave.v.ts";
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), sourceName.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(
            new GpuCompilationRequest([new GpuSourceFile(sourceName, source)]));
        Assert.True(module.Success, Diagnostics(module));
        return (source, module, VdMirGraphicsBackend.Compile(module));
    }

    private static string Diagnostics(VdMirGraphicsModule module)
        => string.Join(Environment.NewLine, module.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
