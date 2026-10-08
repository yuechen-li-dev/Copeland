using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class RayQueryShaderTests
{
    private static string Source => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "RayQuery.v.ts"));

    [Fact]
    public void ProductionAssetPortsCompilerOwnedOctQueryAndCompilesToRealRayQuerySpirv()
    {
        var module = GpuComputeBinder.Compile(new([new("RayQuery.v.ts", Source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal("compute.rayquery.m2", module.FeatureLevel);
        Assert.Equal("acceleration_structure", module.Resources[0].ElementType);
        var compiled = VdMirComputeBackend.Compile(module);
        Assert.NotEmpty(compiled.Spirv);
        Assert.True(compiled.SpirvValidated, compiled.SpirvValidationOutput);
        Assert.Contains("cs_6_5", compiled.DxcArguments);
        Assert.Contains("OpCapability RayQueryKHR", compiled.SpirvDisassembly);
        Assert.Contains("OpRayQueryProceedKHR", compiled.SpirvDisassembly);
        Assert.Contains("OpRayQueryGetIntersectionTKHR", compiled.SpirvDisassembly);
    }

    [Theory]
    [InlineData("readonly Scene", "readwrite Scene")]
    [InlineData("readwrite Hits", "readonly Hits")]
    [InlineData("RayQueryTraceClosest(Scene, Spheres, Rays, Hits, Triangles, thread.x);", "const copy: f32 = RayQueryTraceClosest(Scene, Spheres, Rays, Hits, Triangles, thread.x);")]
    public void InvalidResourceAccessAndOpaqueValueEscapeAreRejected(string before, string after)
    {
        var module = GpuComputeBinder.Compile(new([new("invalid.v.ts", Source.Replace(before, after, StringComparison.Ordinal))]));
        Assert.False(module.Success);
        Assert.NotEmpty(module.Diagnostics);
    }

    [Fact]
    public void AccelerationStructureCannotEscapeIntoLocalStorage()
    {
        string invalid = Source.Replace("RayQueryTraceClosest(Scene,", "const local: acceleration_structure = Scene;\n    RayQueryTraceClosest(Scene,", StringComparison.Ordinal);
        var module = GpuComputeBinder.Compile(new([new("invalid.v.ts", invalid)]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4213");
    }
}
