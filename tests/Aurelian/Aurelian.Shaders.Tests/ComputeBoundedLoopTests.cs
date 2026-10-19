using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ComputeBoundedLoopTests
{
    private static VdMirComputeModule Compile(string body, string before = "")
    {
        return GpuComputeBinder.Compile(new([new GpuSourceFile("bounded.v.ts", $$"""
            @compute @numthreads(64, 1, 1)
            function Main(@builtin(groupIndex) lane: u32,
                @workgroup readwrite Shared: WorkgroupArray<f32,64>,
                @binding(0) readonly Count: StorageBuffer<u32>,
                @binding(1) readwrite Output: StorageBuffer<f32>): void {
                {{before}}
                {{body}}
            }
            """)]));
    }

    [Fact]
    public void UniformReadonlyCountsKeepRuntimeLoopsAndValidatedBarriers()
    {
        var module = Compile("""
            let count = Count[0];
            var sum: f32 = 0.0;
            for (let tile of BoundedIndices<512>(count)) {
                Shared[lane] = Convert<f32>(tile);
                WorkgroupBarrier();
                sum = sum + Shared[lane];
                WorkgroupBarrier();
            }
            Output[lane] = sum;
            """);
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
        Assert.Contains("[loop] for", backend.Hlsl);
        Assert.Contains("512u)", backend.Hlsl);
        Assert.Contains("OpLoopMerge", backend.SpirvDisassembly);
        Assert.Contains("OpControlBarrier", backend.SpirvDisassembly);
    }

    [Theory]
    [InlineData("for (let tile of BoundedIndices<513>(1)) {}", "", "COPE-GPU-LOOP-0001")]
    [InlineData("for (let tile of BoundedIndices<0>(1)) {}", "", "COPE-GPU-LOOP-0001")]
    [InlineData("for (var tile of BoundedIndices<4>(1)) {}", "", "COPE-GPU-LOOP-0001")]
    [InlineData("for (let tile of BoundedIndices<4>(lane)) {}", "", "COPE-GPU-LOOP-0002")]
    [InlineData("for (let tile of BoundedIndices<4>(count)) {}", "var count: u32 = 2;", "COPE-GPU-LOOP-0002")]
    [InlineData("for (let tile of BoundedIndices<4>(count)) { WorkgroupBarrier(); }", "let count: u32 = 1; var count: u32 = lane;", "COPE-GPU-LOOP-0002")]
    [InlineData("for (let tile of BoundedIndices<4>(count)) { WorkgroupBarrier(); }", "for (let first of BoundedIndices<4>(1)) { let count: u32 = 1; } let count: u32 = lane;", "COPE-GPU-LOOP-0002")]
    [InlineData("for (let tile of BoundedIndices<4>(Count[lane])) {}", "", "COPE-GPU-LOOP-0002")]
    [InlineData("for (let tile of BoundedIndices<4>(1)) { return; }", "", "COPE-GPU-LOOP-0003")]
    [InlineData("for (let tile of BoundedIndices<4>(1)) { tile = 2; }", "", "COPE-GPU-MUTATION-0001")]
    [InlineData("if (lane == 0) { for (let tile of BoundedIndices<4>(1)) { WorkgroupBarrier(); } }", "", "COPE-GPU-WORKGROUP-0002")]
    [InlineData("if (lane == 0) { return; } for (let tile of BoundedIndices<4>(1)) { WorkgroupBarrier(); }", "", "COPE-GPU-WORKGROUP-0002")]
    [InlineData("for (let tile of BoundedIndices<4>(1)) { if (lane == 0) { WorkgroupBarrier(); } }", "", "COPE-GPU-WORKGROUP-0002")]
    public void InvalidCountsAndDivergentSynchronizationFailBeforeEmission(string body, string before, string diagnostic)
    {
        var module = Compile(body, before);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == diagnostic);
    }
}
