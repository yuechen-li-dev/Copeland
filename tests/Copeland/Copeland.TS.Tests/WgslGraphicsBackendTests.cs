using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class WgslGraphicsBackendTests
{
    internal const string Source = """
        @space(clip.position) type Clip = float4;
        stream Input { @location(0) position: float3; }
        stream Varyings { @builtin(position) position: Clip; @location(0) local: float3; }
        stream Output { @target(0) color: float4; @builtin(frag_depth) depth: f32; }
        @vertex function VertexMain(input: Input): Varyings {
            return { position: float4(input.position, 1.0), local: input.position };
        }
        function radius(x: f32, y: f32): f32 { return Sqrt(x * x + y * y); }
        @pixel function PixelMain(input: Varyings): Output {
            var distance: f32 = radius(input.local.x, input.local.y);
            for (var i: u32 = 0; i < 4; i = i + 1) {
                if (!(distance > 1.0) && true) { distance = distance + 0.01; }
                else { if (distance > 2.0) { break; } }
            }
            if (distance > 1.0) { Discard(); }
            return { color: float4(distance, 0.0, 0.0, 1.0), depth: distance / 3.0 };
        }
        """;

    [Fact]
    public void DirectCompilerPreservesControlFlowIoAndTraceability()
    {
        var result = Compile(Source);
        Assert.True(result.Success, Diagnostics(result));
        var program = result.Program!;
        Assert.Contains("@builtin(frag_depth)", program.Code);
        Assert.Contains("@vertex", program.Code);
        Assert.Contains("@fragment", program.Code);
        Assert.Contains("for (var", program.Code);
        Assert.Contains("discard;", program.Code);
        Assert.Contains("} else {", program.Code);
        Assert.Contains("let ", program.Code);
        Assert.Contains("var ", program.Code);
        Assert.All(program.SourceMappings, mapping => Assert.Equal("shader.v.ts", mapping.Source.File));
    }

    [Fact]
    public void CanonicalBindingsAndUniformByteLayoutAreRetained()
    {
        var result = Compile(GpuGraphicsBinderM3Tests.Source);
        Assert.True(result.Success, Diagnostics(result));
        Assert.Equal([0, 1, 2], result.Program!.Semantics.Resources.Select(resource => resource.Binding));
        Assert.Contains("@group(0) @binding(2) var<uniform>", result.Program.Code);
        Assert.Contains("texture_2d<f32>", result.Program.Code);
        Assert.Contains("textureSample(", result.Program.Code);
        Assert.Contains("@size(16) m1: f32", result.Program.Code);
        Assert.Equal(32, result.Program.Semantics.Material!.Size);
        Assert.Equal(result.Program.Code, Compile(GpuGraphicsBinderM3Tests.Source).Program!.Code);
    }

    [Fact]
    public void HashIgnoresSourcePathsAndWhitespaceButChangesWithSemantics()
    {
        var first = Compile(Source).Program!;
        var relocated = WgslGraphicsBackend.Compile(new GpuCompilationRequest([new GpuSourceFile("elsewhere.v.ts", "\n\n" + Source)])).Program!;
        Assert.Equal(first.SemanticHash, relocated.SemanticHash);
        Assert.Equal(first.Code, relocated.Code);
        Assert.Equal(first.SemanticHash, Compile(Source.Replace("0.01", "0.0100", StringComparison.Ordinal)).Program!.SemanticHash);
        Assert.NotEqual(first.SemanticHash, Compile(Source.Replace("distance / 3.0", "distance / 4.0", StringComparison.Ordinal)).Program!.SemanticHash);
    }

    [Fact]
    public void DerivativeSamplingAtVertexStageFailsBeforeSerialization()
    {
        string source = GpuGraphicsBinderM3Tests.Source
            .Replace("input: VertexInput, builtins: VertexBuiltins", "input: VertexInput, builtins: VertexBuiltins, resources: ForwardResources", StringComparison.Ordinal)
            .Replace("const vertexBias: f32", "const sampled: float4 = Sample(resources.albedo, resources.linearSampler, input.uv); const vertexBias: f32", StringComparison.Ordinal);
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "COPE-WGSL-STAGE-0001");
    }

    [Fact]
    public void ReservedSourceNamesAndBuiltinNamesCannotCollideWithGeneratedIdentifiers()
    {
        string source = Source.Replace("distance", "filter", StringComparison.Ordinal)
            .Replace("radius", "textureSample", StringComparison.Ordinal);
        var result = Compile(source);
        Assert.True(result.Success, Diagnostics(result));
        Assert.DoesNotContain("filter", result.Program!.Code);
        Assert.DoesNotContain("textureSample", result.Program.Code);
    }

    [Fact]
    public void InvalidFrontendAndUnsupportedMirSurfaceSourceDiagnostics()
    {
        var invalid = Compile(Source.Replace("i < 4", "i < 9999", StringComparison.Ordinal));
        Assert.False(invalid.Success);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-LOOP-0001");
        var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("shader.v.ts", Source)]));
        var function = module.Functions.First();
        var unsupported = WgslGraphicsBackend.Lower(module with
        {
            Functions = [function with { ReturnType = "float4x4" }, .. module.Functions.Skip(1)],
        });
        Assert.False(unsupported.Success);
        Assert.Equal("COPE-WGSL-TYPE-0001", Assert.Single(unsupported.Diagnostics).Code);
        Assert.Equal("shader.v.ts", unsupported.Diagnostics[0].PrimarySpan.File);
    }

    private static WgslGraphicsResult Compile(string source)
        => WgslGraphicsBackend.Compile(new GpuCompilationRequest([new GpuSourceFile("shader.v.ts", source)]));

    private static string Diagnostics(WgslGraphicsResult result)
        => string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Message));
}
