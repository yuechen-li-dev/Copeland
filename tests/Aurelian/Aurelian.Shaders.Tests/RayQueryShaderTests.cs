using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Library;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class RayQueryShaderTests
{
    [Theory]
    [InlineData("SampledLocalLights3D.v.ts")]
    [InlineData("RestirSpatial3D.v.ts")]
    public void SampledLocalLightingUsesTheTypedOcclusionBoundary(string name)
    {
        var module = GpuComputeBinder.Compile(new(BuiltinShaderLibrary.LoadSources(name)));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var compiled = VdMirComputeBackend.Compile(module);
        Assert.True(compiled.SpirvValidated, compiled.DxcOutput + compiled.SpirvValidationOutput);
        Assert.Equal("compute.rayquery.m4", module.FeatureLevel);
    }

    [Fact]
    public void LocalOcclusionKernelTerminatesTraversalAndValidatesSpirv()
    {
        var module = GpuComputeBinder.Compile(new(BuiltinShaderLibrary.LoadSources("LocalShadows3D.v.ts")));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var compiled = VdMirComputeBackend.Compile(module);
        Assert.True(compiled.SpirvValidated, compiled.DxcOutput + compiled.SpirvValidationOutput);
        Assert.Equal("compute.rayquery.m4", module.FeatureLevel);
        Assert.Contains("OpRayQueryTerminateKHR", compiled.SpirvDisassembly);
        Assert.Contains("CandidateInstanceID", compiled.Hlsl);
    }

    [Fact]
    public void OcclusionRequiresItsClosedEvaluatorRatherThanSilentlyIgnoringProcedurals()
    {
        var sources = BuiltinShaderLibrary.LoadSources("LocalShadows3D.v.ts")
            .Select(source => source with { Source = source.Source.Replace("index, AffineAnalyticIntersection)", "index)", StringComparison.Ordinal) });
        var module = GpuComputeBinder.Compile(new(sources.ToArray()));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-RAYQUERY-0001");
    }

    [Fact]
    public void AffineCandidatesUseTheSameIntersectionModuleAsRasterization()
    {
        var sources = BuiltinShaderLibrary.LoadSources("AffineRayQuery3D.v.ts");
        Assert.Contains(sources, source => source.Path == "AnalyticIntersections3D.v.ts");
        var module = GpuComputeBinder.Compile(new(sources));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        Assert.Equal("compute.rayquery.m3", module.FeatureLevel);
        var compiled = VdMirComputeBackend.Compile(module);
        Assert.True(compiled.SpirvValidated, compiled.DxcOutput + compiled.SpirvValidationOutput);
        Assert.Contains("OpRayQueryProceedKHR", compiled.SpirvDisassembly);
        Assert.Contains("IntersectAnalyticRange", compiled.Hlsl);
    }

    [Theory]
    [InlineData("readonly primitives", "readwrite primitives")]
    [InlineData("mask: u32;", "mask: f32;")]
    public void ClosedIntersectorRejectsMutationAndInvalidHitContracts(string before, string after)
    {
        var sources = BuiltinShaderLibrary.LoadSources("AffineRayQuery3D.v.ts")
            .Select(source => source.Path == "AffineAnalyticQuery3D.v.ts"
                ? source with { Source = source.Source.Replace(before, after, StringComparison.Ordinal) }
                : source).ToArray();
        var module = GpuComputeBinder.Compile(new(sources));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-RAYQUERY-0004");
    }

    [Fact]
    public void IntersectorOperandIsAClosedSymbolRatherThanAnOrdinaryFunctionValue()
    {
        var sources = BuiltinShaderLibrary.LoadSources("AffineRayQuery3D.v.ts")
            .Select(source => source.Path == "AffineRayQuery3D.v.ts"
                ? source with { Source = source.Source.Replace("thread.x, AffineAnalyticIntersection)", "thread.x, 1)", StringComparison.Ordinal) }
                : source).ToArray();
        var module = GpuComputeBinder.Compile(new(sources));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-RAYQUERY-0004");
    }

    [Fact]
    public void ReferenceKernelUsesSharedLightingAndCompilesToValidatedRayQuerySpirv()
    {
        var module = GpuComputeBinder.Compile(new(BuiltinShaderLibrary.LoadSources("ReferenceIntegrator3D.v.ts")));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var compiled = VdMirComputeBackend.Compile(module);
        Assert.True(compiled.SpirvValidated, compiled.DxcOutput + compiled.SpirvValidationOutput);
        Assert.Contains("OpRayQueryProceedKHR", compiled.SpirvDisassembly);
        Assert.Contains("RWStructuredBuffer<float> Rays", compiled.Hlsl);
    }

    [Fact]
    public void ResidentRayPacketsCompileWithoutExposingOpaqueQueryState()
    {
        string source = Source.Replace("readonly Rays", "readwrite Rays", StringComparison.Ordinal);
        var module = GpuComputeBinder.Compile(new([new("resident.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var compiled = VdMirComputeBackend.Compile(module);
        Assert.True(compiled.SpirvValidated, compiled.SpirvValidationOutput);
        Assert.Contains("OpRayQueryProceedKHR", compiled.SpirvDisassembly);
    }

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
