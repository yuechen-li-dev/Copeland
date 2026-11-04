using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Aurelian.Shaders.Graphics;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class GraphicsHitKindTests
{
    [Fact]
    public void UnsignedHitKindBitsCompileAsIntegerOperationsInBothStages()
    {
        const string source = """
            @space(clip.position)
            type Clip = float4;
            stream Input { @location(0) position: float2; }
            stream Varyings { @builtin(position) position: Clip; @location(0) @interpolation(flat) kind: u32; }
            stream Output { @target(0) identity: uint2; }
            @vertex
            function VertexMain(input: Input): Varyings {
                let kind: u32 = (1 << 31) | 3;
                return { position: float4(input.position.x, input.position.y, 0.0, 1.0), kind: kind };
            }
            @pixel
            function PixelMain(input: Varyings): Output {
                let tag: u32 = input.kind & 0x80000000;
                let payload: u32 = (input.kind ^ tag) >> 1;
                return { identity: uint2(tag, payload) };
            }
            """;
        var module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new("hit-kind.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirGraphicsBackend.Compile(module);
        Assert.True(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated,
            backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
        var invalid = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new("bad.v.ts", source.Replace(
            "input.kind & 0x80000000", "1.0 & 2.0", StringComparison.Ordinal))]));
        Assert.False(invalid.Success);
        Assert.Contains(invalid.Diagnostics, item => item.Code == "COPE-GPU-OPERATOR-0001");
    }
}
