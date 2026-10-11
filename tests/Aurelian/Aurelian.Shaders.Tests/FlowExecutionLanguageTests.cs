using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class FlowExecutionLanguageTests
{
    [Theory]
    [InlineData("match")]
    [InlineData("switch")]
    public void Case_prefixes_preserve_gpu_payload_emission(string spelling)
    {
        string source = $$"""
            enum Choice { Empty, Value(value: u32) }
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite Output: StorageBuffer<f32>): void {
                const value: u32 = {{spelling}} Choice.Value(7) {
                    case Choice.Empty => 0,
                    case Choice.Value(payload) => payload.value,
                };
                Output[0] = Convert<f32>(value);
            }
            """;
        var prefixed = GpuComputeBinder.Compile(new([new GpuSourceFile("case.v.ts", source)]));
        var original = GpuComputeBinder.Compile(new([new GpuSourceFile("case.v.ts", source.Replace("case ", "", StringComparison.Ordinal))]));
        Assert.True(prefixed.Success);
        Assert.True(original.Success);
        Assert.Equal(VdMirComputeHlslEmitter.Emit(original), VdMirComputeHlslEmitter.Emit(prefixed));
    }

    [Theory]
    [InlineData("enum Invalid { Next(child: Invalid) }")]
    [InlineData("enum Invalid { Resource(buffer: StorageBuffer<f32>) }")]
    public void Recursive_or_opaque_frame_payloads_are_rejected(string declaration)
    {
        string source = declaration + """
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite Output: StorageBuffer<f32>): void { Output[0] = 0.0; }
            """;
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("invalid.v.ts", source)]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-ENUM-0002");
    }

    [Fact]
    public void Typed_child_frames_compile_to_validated_compute_spirv()
    {
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("children.v.ts", """
            flow Child(input: u32) -> u32 ! u32 {
                board { remaining: u32 = input; }
                state Work initial {
                    if (board.remaining == 0) { finish input; }
                    board.remaining = board.remaining - 1;
                    yield;
                }
            }
            flow Parent(input: u32) -> u32 {
                state Begin initial { run Child(input) -> Accept ! Reject; }
                state Accept(value: u32) { finish value + 10; }
                state Reject(error: u32) { finish error + 100; }
            }
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite Output: StorageBuffer<f32>): void {
                var frame = Parent.start(2);
                frame = Parent.step(frame);
                frame = Parent.step(frame);
                const result: u32 = match frame.outcome {
                    case ParentOutcome.Active => 1000,
                    case ParentOutcome.Completed(payload) => payload.value,
                    case ParentOutcome.Fault(payload) => payload.code + 2000,
                };
                Output[0] = Convert<f32>(result);
            }
            """)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Code + ": " + item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
    }

    [Fact]
    public void Explicit_execution_steps_compile_to_validated_compute_spirv()
    {
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("flow.v.ts", """
            flow Countdown(limit: u32) -> u32 {
                board { remaining: u32 = limit; }
                state Counting initial {
                    if (board.remaining == 0) { finish limit; }
                    board.remaining = board.remaining - 1;
                    yield;
                }
            }
            @compute @numthreads(1, 1, 1)
            function Main(@binding(0) readwrite Output: StorageBuffer<f32>): void {
                var frame = Countdown.start(2);
                frame = Countdown.step(frame);
                frame = Countdown.step(frame);
                frame = Countdown.step(frame);
                Output[0] = Convert<f32>(frame.board.remaining);
            }
            """)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Code + ": " + item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
    }
}
