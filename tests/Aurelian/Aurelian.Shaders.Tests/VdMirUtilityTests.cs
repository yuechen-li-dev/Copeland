using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VdMirUtilityTests
{
    [Fact]
    public void GuardedUtilityUsesOrdinaryHlslAndValidatesBothShaderStages()
    {
        const string source = """
            @space(clip.position) type Clip = float4;
            stream Input { @location(0) position: float3; }
            stream Varyings { @builtin(position) position: Clip; @location(0) evidence: float3; }
            stream Output { @target(0) color: float4; }
            @vertex function VertexMain(input: Input): Varyings {
                return { position: float4(input.position, 1.0), evidence: input.position };
            }
            @pixel function PixelMain(input: Varyings): Output {
                const weight: f32 = when utility {
                    case 0.0 when true score 1
                    case 0.5 when input.evidence.x > 0.8 score 3
                    case 0.25 when input.evidence.x > 0.5 score 2
                    else 0.0
                };
                return { color: float4(weight, weight, weight, 1.0) };
            }
            """;
        var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new("utility.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(d => d.Message)));
        string hlsl = VdMirGraphicsHlslEmitter.Emit(module);
        Assert.Contains("utilityWinner", hlsl);
        Assert.DoesNotContain("when utility", hlsl);
        var result = VdMirGraphicsBackend.Compile(module);
        Assert.True(result.Vertex.SpirvValidated, result.Vertex.DxcOutput + result.Vertex.SpirvValidationOutput);
        Assert.True(result.Pixel.SpirvValidated, result.Pixel.DxcOutput + result.Pixel.SpirvValidationOutput);
    }
}
