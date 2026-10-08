using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class StaticModelShaderTests
{
    [Fact]
    public void StaticMaterialNestedHelpersAndMaskCompileThroughTheCanonicalBackend()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "StaticModel3D.v.ts"));
        var module = GpuGraphicsBinder.Compile(new([new("StaticModel3D.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        var compiled = CompiledGraphicsProgramExporter.Export(module, backend);
        Assert.Equal(144, compiled.Material!.Size);
        Assert.Equal(11, compiled.Resources.Count);
        Assert.Equal(5, compiled.VertexInputs.Count);
        Assert.Contains("OpKill", backend.Pixel.SpirvDisassembly!);
        Assert.DoesNotContain("DemoteToHelperInvocation", backend.Pixel.SpirvDisassembly!);
    }
}
