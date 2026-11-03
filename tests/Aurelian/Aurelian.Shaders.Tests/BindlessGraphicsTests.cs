using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class BindlessGraphicsTests
{
    [Theory]
    [InlineData("SurfaceModel3D.v.ts")]
    [InlineData("BindlessModel3D.v.ts")]
    [InlineData("VisibilityRaster3D.v.ts")]
    [InlineData("VisibilityResolve3D.v.ts")]
    public void SharedSurfacePathsCompileThroughTheCanonicalBackend(string name)
    {
        var module = GpuGraphicsBinder.Compile(new(BuiltinShaderLibrary.LoadSources(name)));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        if (name == "BindlessModel3D.v.ts")
        {
            Assert.Equal(VdMirGraphicsModule.GraphicsTablesM1FeatureLevel, module.FeatureLevel);
            var program = CompiledGraphicsProgramExporter.Export(module, backend);
            Assert.Equal(4096, program.Resources.Single(resource => resource.Name == "textures").DescriptorCount);
            Assert.Equal(128, program.Resources.Single(resource => resource.Name == "materials").StorageLayout!.Stride);
            Assert.Contains("NonUniform", backend.Pixel.SpirvDisassembly);
        }
    }

    [Theory]
    [InlineData("NonUniform(material.baseTexture)", "material.baseTexture", "COPE-GPU-TABLE-0003")]
    [InlineData("textures[NonUniform(material.baseTexture)]", "textures[4096]", "COPE-GPU-TABLE-0004")]
    [InlineData("float4>, 4096>", "float4>, 0>", "COPE-GPU-TABLE-0001")]
    [InlineData("reserved0: u32", "reserved0: bool", "COPE-GPU-TABLE-0002")]
    public void InvalidTableContractsFailBeforeNativeExecution(string original, string replacement, string code)
    {
        var sources = BuiltinShaderLibrary.LoadSources("BindlessModel3D.v.ts").Select(source =>
            source with { Source = source.Source.Replace(original, replacement, StringComparison.Ordinal) }).ToArray();
        var module = GpuGraphicsBinder.Compile(new(sources));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }
}
