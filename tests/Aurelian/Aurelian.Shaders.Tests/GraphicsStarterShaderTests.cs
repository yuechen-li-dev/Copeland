using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class GraphicsStarterShaderTests
{
    [Theory]
    [InlineData("Solid3D.v.ts", 240, 3)]
    [InlineData("Shadow3D.v.ts", 64, 1)]
    [InlineData("ToneMap3D.v.ts", 16, 3)]
    public void StarterPassesCompileToValidatedSpirvWithTheirResourceContracts(string name, int uniformBytes, int resourceCount)
    {
        var sources = new List<GpuSourceFile> { Read(name) };
        if (name == "Solid3D.v.ts")
        {
            sources.Add(Read("Lighting3D.v.ts"));
        }
        var module = GpuGraphicsBinder.Compile(new(sources));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        var compiled = CompiledGraphicsProgramExporter.Export(module, backend);
        Assert.Equal(uniformBytes, compiled.Material!.Size);
        Assert.Equal(resourceCount, compiled.Resources.Count);
        if (name == "ToneMap3D.v.ts")
        {
            Assert.Contains("Pow", backend.Pixel.SpirvDisassembly);
        }
    }

    [Fact]
    public void PowerIntrinsicRejectsVectorArgumentsWithATypeDiagnostic()
    {
        GpuSourceFile source = Read("ToneMap3D.v.ts");
        var module = GpuGraphicsBinder.Compile(new([source with
        {
            Source = source.Source.Replace("Pow(x, 1.0 / 2.4)", "Pow(float2(x, x), 1.0 / 2.4)", StringComparison.Ordinal),
        }]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-MATH-0001");
    }

    private static GpuSourceFile Read(string name) => new(name,
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", name)));
}
