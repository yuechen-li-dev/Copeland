using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ImplicitGraphicsTests
{
    public const string Source = """
        @space(clip.position)
        type ClipPosition = float4;
        stream Input {
            @location(0) position: float3;
        }
        stream Varyings {
            @builtin(position) position: ClipPosition;
            @location(0) local: float3;
        }
        stream Output {
            @target(0) color: float4;
            @builtin(frag_depth) depth: f32;
        }
        @vertex
        function VertexMain(input: Input): Varyings {
            return { position: float4(input.position.x, input.position.y, 0.0, 1.0), local: input.position };
        }
        function Field(x: f32, y: f32, z: f32): f32 {
            return Sqrt(x * x + y * y + z * z) - 1.0;
        }
        @pixel
        function PixelMain(input: Varyings): Output {
            var t: f32 = 0.0;
            for (var i: u32 = 0; i < 128; i = i + 1) {
                const d: f32 = Field(input.local.x, input.local.y, -3.0 + t);
                if (Abs(d) < 0.00001) {
                    return { color: float4(0.8, 0.6, 0.2, 1.0), depth: t / 6.0 };
                }
                t = t + Abs(d);
                if (t > 6.0) {
                    break;
                }
            }
            Discard();
            return { color: float4(0.0, 0.0, 0.0, 1.0), depth: 1.0 };
        }
        """;

    [Fact]
    public void TraversalAndDepthCompileThroughExistingDxcBackend()
    {
        VdMirGraphicsModule module = Bind(Source);
        Assert.True(module.Success, Diagnostics(module));
        VdMirGraphicsBackendResult backend = VdMirGraphicsBackend.Compile(module);
        Assert.True(backend.Pixel.SpirvValidated, backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        Assert.True(backend.Vertex.SpirvValidated, backend.Vertex.DxcOutput + backend.Vertex.SpirvValidationOutput);
        Assert.Contains("SV_Depth", backend.Hlsl);
        Assert.Contains("for (uint i", backend.Hlsl);
        Assert.Contains("discard;", backend.Hlsl);
        var program = CompiledGraphicsProgramExporter.Export(module, backend);
        Assert.Single(program.PixelTargets);
        Assert.Equal("frag_depth", program.FragmentDepth?.Builtin);
    }

    [Fact]
    [Trait("Category", "WebGpuToolchain")]
    public void DxcSpirvTranslatesToValidatedBrowserWgsl()
    {
        // This explicit toolchain test must fail when Naga is unavailable, never silently skip.
        var module = Bind(Source);
        Assert.True(module.Success, Diagnostics(module));
        var program = WebGpuGraphicsBackend.Compile(module);
        Assert.Contains("@builtin(frag_depth)", program.FragmentWgsl);
        Assert.Contains("@fragment", program.FragmentWgsl);
        Assert.Contains("@vertex", program.VertexWgsl);
    }

    [Theory]
    [InlineData("i < 128", "i < 10000", "COPE-GPU-LOOP-0001")]
    [InlineData("t = t + Abs(d);", "i = i + 1;", "COPE-GPU-MUTATION-0001")]
    [InlineData("var t: f32", "const t: f32", "COPE-GPU-MUTATION-0001")]
    [InlineData("depth: f32;", "depth: float4;", "COPE-GPU-TARGET-0003")]
    [InlineData("i < 128", "i < input.local.x", "COPE-GPU-LOOP-0001")]
    [InlineData("i = i + 1)", "i = i + 2)", "COPE-GPU-LOOP-0001")]
    [InlineData("@builtin(frag_depth) depth: f32;", "@builtin(frag_depth) depth: f32; @builtin(frag_depth) anotherDepth: f32;", "COPE-GPU-BUILTIN-0002")]
    public void InvalidProgramsFailClosed(string before, string after, string code)
    {
        var module = Bind(Source.Replace(before, after, StringComparison.Ordinal));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void DepthIsRejectedAtVertexStageAndDiscardCannotRunAtVertexStage()
    {
        string source = Source.Replace("@location(0) position: float3;", "@builtin(frag_depth) position: f32;", StringComparison.Ordinal);
        Assert.False(Bind(source).Success);
        source = Source.Replace("function VertexMain(input: Input): Varyings {", "function VertexMain(input: Input): Varyings { Discard();", StringComparison.Ordinal);
        Assert.False(Bind(source).Success);
    }

    [Fact]
    public void BranchLocalsDoNotEscapeAndBreakRequiresALoop()
    {
        string source = Source.Replace("t = t + Abs(d);", "if (d > 0.0) { const hidden: f32 = d; } t = hidden;", StringComparison.Ordinal);
        Assert.False(Bind(source).Success);
        source = Source.Replace("Discard();", "break;", StringComparison.Ordinal);
        Assert.False(Bind(source).Success);
    }

    [Fact]
    public void BooleanLiteralsAndNegationRetainTypedM4Semantics()
    {
        string source = Source.Replace("Abs(d) < 0.00001", "!(d > 0.00001) && true", StringComparison.Ordinal);
        var module = Bind(source);
        Assert.True(module.Success, Diagnostics(module));
        Assert.Equal(VdMirGraphicsModule.GraphicsM4FeatureLevel, module.FeatureLevel);
    }

    private static VdMirGraphicsModule Bind(string source)
        => GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("implicit.v.ts", source)]));

    private static string Diagnostics(VdMirGraphicsModule module)
        => string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => diagnostic.Message));
}
