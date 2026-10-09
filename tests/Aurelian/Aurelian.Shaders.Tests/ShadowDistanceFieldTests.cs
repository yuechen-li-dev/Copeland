using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ShadowDistanceFieldTests
{
    [Theory]
    [InlineData("Field")]
    [InlineData("Raster")]
    [InlineData("Reference")]
    [InlineData("Depth")]
    public void Research_variants_compile_with_the_actual_static_model_resource_layout(string mode)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets");
        var sources = GpuSourceLoader.Load("ShadowExperimentModel.v.ts", name =>
        {
            string actual = name == "ShadowReconstruction.v.ts" ? "Shadow" + mode + ".v.ts" : name;
            string path = Path.Combine(directory, actual);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        });
        var module = GpuGraphicsBinder.Compile(new(sources));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        var program = CompiledGraphicsProgramExporter.Export(module, backend);
        Assert.Equal(288, program.Material!.Size);
        Assert.Equal(13, program.Resources.Count);
        if (mode == "Field")
        {
            Assert.Contains("fwidth(", backend.Hlsl, StringComparison.Ordinal);
            Assert.Contains("DistanceFieldShadowVisibility", backend.Hlsl, StringComparison.Ordinal);
        }
    }
}
