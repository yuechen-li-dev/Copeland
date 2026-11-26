using Aurelian.Shaders.Graphics;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VdMirUtilityTests
{
    [Fact]
    public void ComputeChoiceCompilesWithLazyStorageReadsAndSharedArrayAccess()
    {
        const string source = """
            @compute @numthreads(8, 1, 1)
            function Main(@builtin(groupIndex) lane: u32,
                @workgroup readwrite Shared: WorkgroupArray<f32, 8>,
                @binding(0) readonly Input: StorageBuffer<f32>,
                @binding(1) readwrite Output: StorageBuffer<f32>): void {
                Shared[lane] = Input[lane];
                WorkgroupBarrier();
                Output[lane] = when utility {
                    case Input[lane - 1] when lane > 0 score 3
                    case Shared[lane] when true score 1
                    else 0.0
                };
            }
            """;
        var module = GpuComputeBinder.Compile(new GpuCompilationRequest([new("utility-compute.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(d => d.Message)));
        var helper = Assert.Single(module.Functions, function => function.Name.StartsWith("VtsUtility", StringComparison.Ordinal));
        Assert.Equal(new[] { "lane", "Input" }, helper.Parameters.Select(parameter => parameter.Name));
        var result = VdMirComputeBackend.Compile(module);
        Assert.True(result.SpirvValidated, result.DxcOutput + result.SpirvValidationOutput);
    }

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
                let weight: f32 = when utility {
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
