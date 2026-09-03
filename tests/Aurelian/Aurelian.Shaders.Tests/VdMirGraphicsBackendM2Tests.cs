using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Language.External.Dxc;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VdMirGraphicsBackendM2Tests
{
    private const string Source = """
        stream VertexInput { @location(0) position: float3; @location(1) uv: float2; }
        stream VertexOutput { @builtin(position) position: float4; @location(0) uv: float2; }
        stream PixelInput { @location(0) uv: float2; }
        stream PixelOutput { @target(0) color: float4; }
        function PassUv(value: float2): float2 { return value; }
        @vertex function VertexMain(input: VertexInput): VertexOutput {
            return { position: float4(input.position, 1.0), uv: PassUv(input.uv), };
        }
        @pixel function PixelMain(input: PixelInput): PixelOutput {
            return { color: float4(PassUv(input.uv), 0.0, 1.0), };
        }
        """;

    [Fact]
    public void Streams_Generate_Backend_Structs_And_Semantics()
    {
        string hlsl = VdMirGraphicsHlslEmitter.Emit(Compile());

        Assert.Contains("struct VertexInput", hlsl);
        Assert.Contains("float3 position : TEXCOORD0;", hlsl);
        Assert.Contains("float4 position : SV_Position;", hlsl);
        Assert.Contains("float4 color : SV_Target0;", hlsl);
        Assert.Contains("VertexOutput VertexMain(VertexInput input)", hlsl);
        Assert.Contains("PixelOutput PixelMain(PixelInput input)", hlsl);
        Assert.DoesNotContain("SV_Position", Source);
        Assert.DoesNotContain("TEXCOORD", Source);
    }

    [Fact]
    public void Dxc_Compiles_Both_Stages_Validates_Spirv_And_Repeats_Hashes()
    {
        if (!DxcExecutableResolver.Resolve().Success)
        {
            return;
        }

        VdMirGraphicsBackendResult first = VdMirGraphicsBackend.Compile(Compile());
        VdMirGraphicsBackendResult second = VdMirGraphicsBackend.Compile(Compile());

        Assert.True(first.Vertex.DxcStatus == DxcSpirvStatus.Compiled, first.Vertex.DxcOutput);
        Assert.True(first.Pixel.DxcStatus == DxcSpirvStatus.Compiled, first.Pixel.DxcOutput);
        Assert.True(first.Vertex.SpirvValidated, first.Vertex.SpirvValidationOutput);
        Assert.True(first.Pixel.SpirvValidated, first.Pixel.SpirvValidationOutput);
        Assert.Equal(first.HlslSha256, second.HlslSha256);
        Assert.Equal(first.Vertex.SpirvSha256, second.Vertex.SpirvSha256);
        Assert.Equal(first.Pixel.SpirvSha256, second.Pixel.SpirvSha256);
        Assert.Contains("OpEntryPoint Vertex", first.Vertex.SpirvDisassembly);
        Assert.Contains("OpEntryPoint Fragment", first.Pixel.SpirvDisassembly);
        Assert.Contains("BuiltIn Position", first.Vertex.SpirvDisassembly);
        Assert.Contains("Location 0", first.Vertex.SpirvDisassembly);
        Assert.Contains("Location 0", first.Pixel.SpirvDisassembly);
        Assert.Contains("vs_6_0", first.Vertex.DxcArguments);
        Assert.Contains("ps_6_0", first.Pixel.DxcArguments);
    }

    [Fact]
    public void Legacy_Heuristic_Frontend_Is_Not_On_Graphics_Backend_Path()
    {
        string[] parameterTypes = typeof(VdMirGraphicsBackend).Assembly.GetTypes()
            .Where(type => type.Namespace == "Aurelian.Shaders.Graphics")
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(parameterTypes, type => type.Contains("SdslvParser", StringComparison.Ordinal));
        Assert.DoesNotContain(parameterTypes, type => type.Contains("SdslvValidator", StringComparison.Ordinal));
    }

    private static VdMirGraphicsModule Compile()
    {
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("GraphicsStreamM2.v.ts", Source)]));
        Assert.True(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return module;
    }
}
