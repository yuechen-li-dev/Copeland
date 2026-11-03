using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VisibilityGraphicsTests
{
    [Fact]
    public void IdentityRasterUsesUnsignedTargetsAndRealPrimitiveAndDrawBuiltins()
    {
        var module = GpuGraphicsBinder.Compile(new(BuiltinShaderLibrary.LoadSources("VisibilityRaster3D.v.ts")));
        Assert.True(module.Success);
        Assert.Equal(VdMirGraphicsModule.GraphicsVisibilityM1FeatureLevel, module.FeatureLevel);
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        Assert.Contains("BuiltIn DrawIndex", backend.Vertex.SpirvDisassembly);
        Assert.Contains("BuiltIn PrimitiveId", backend.Pixel.SpirvDisassembly);
        Assert.Contains("OpCapability Geometry", backend.Pixel.SpirvDisassembly);
        var program = CompiledGraphicsProgramExporter.Export(module, backend);
        Assert.Equal("uint2", Assert.Single(program.PixelTargets).PhysicalType);
    }

    [Fact]
    public void ResolveUsesIntegerLoadsExplicitGradientsAndTheSharedMaterialImplementation()
    {
        var module = GpuGraphicsBinder.Compile(new(BuiltinShaderLibrary.LoadSources("VisibilityResolve3D.v.ts")));
        Assert.True(module.Success);
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        Assert.Contains("OpImageFetch", backend.Pixel.SpirvDisassembly);
        Assert.Contains("OpImageSampleExplicitLod", backend.Pixel.SpirvDisassembly);
        Assert.Contains("Grad", backend.Pixel.SpirvDisassembly);
        Assert.Contains(module.Functions, function => function.Name.Contains("EvaluateSurfaceSamples", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("VisibilityRaster3D.v.ts", "@builtin(draw_id)", "@builtin(primitive_id)", "COPE-GPU-BUILTIN-0003")]
    [InlineData("VisibilityRaster3D.v.ts", "@builtin(primitive_id)", "@builtin(draw_id)", "COPE-GPU-BUILTIN-0003")]
    [InlineData("VisibilityRaster3D.v.ts", "@target(0) identity: uint2", "@target(0) identity: bool", "COPE-GPU-TARGET-0003")]
    [InlineData("VisibilityResolve3D.v.ts", "Load(tables.visibility, uint2(Convert<u32>(input.position.x), Convert<u32>(input.position.y)))",
        "Load(tables.visibility, float2(input.position.x, input.position.y))", "COPE-GPU-IMAGE-0001")]
    [InlineData("VisibilityResolve3D.v.ts", "Load(tables.visibility, uint2(Convert<u32>(input.position.x), Convert<u32>(input.position.y)))",
        "Sample(tables.visibility, tables.samplers[0], float2(0.0, 0.0))", "COPE-GPU-SAMPLE-0002")]
    public void InvalidVisibilityInterfacesFailBeforeBackendExecution(string name, string original, string replacement, string code)
    {
        var sources = BuiltinShaderLibrary.LoadSources(name);
        Assert.Contains(sources, source => source.Source.Contains(original, StringComparison.Ordinal));
        var modified = sources.Select(source => source with
        {
            Source = source.Source.Replace(original, replacement, StringComparison.Ordinal),
        }).ToArray();
        var module = GpuGraphicsBinder.Compile(new(modified));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }
}
