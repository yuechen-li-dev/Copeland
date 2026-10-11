using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ComputeWorkgroupTests
{
    private static VdMirComputeModule Compile(string body, string helpers = "", string shared = "@workgroup readwrite Shared: WorkgroupArray<f32, 64>,") =>
        GpuComputeBinder.Compile(new([new GpuSourceFile("workgroup.v.ts", $$"""
            {{helpers}}
            @compute @numthreads(64, 1, 1)
            function Main(@builtin(groupId) group: uint3, @builtin(groupIndex) lane: u32,
                {{shared}}
                @binding(0) readwrite Output: StorageBuffer<f32>): void {
                {{body}}
            }
            """)]));

    [Fact]
    public void SharedStorageAndRealGroupBuiltinsProduceValidatedControlBarriers()
    {
        var module = Compile("""
            Shared[lane] = Convert<f32>(lane);
            WorkgroupBarrier();
            if (lane == 0) { Output[group.x] = Shared[63]; }
            """);
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        Assert.Single(module.WorkgroupArrays!);
        var backend = VdMirComputeBackend.Compile(module);
        Assert.True(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
        Assert.Contains("OpControlBarrier", backend.SpirvDisassembly);
        Assert.Contains("groupshared float Shared[64]", backend.Hlsl);
        Assert.Contains("SV_GroupID", backend.Hlsl);
        Assert.Contains("SV_GroupIndex", backend.Hlsl);
        Assert.DoesNotContain("Shared :", backend.Hlsl);
    }

    [Theory]
    [InlineData("if (lane == 0) { WorkgroupBarrier(); }")]
    [InlineData("if (lane == 0) { return; } WorkgroupBarrier();")]
    [InlineData("Wait();", "function Wait(): void { WorkgroupBarrier(); }")]
    public void DivergentOrHelperWaitsHaveAnExactRepair(string body, string helpers = "")
    {
        var module = Compile(body, helpers);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-WORKGROUP-0002");
    }

    [Theory]
    [InlineData("@workgroup readonly Shared: WorkgroupArray<f32,64>,")]
    [InlineData("@workgroup readwrite Shared: WorkgroupArray<f32,0>,")]
    [InlineData("@workgroup @binding(1) readwrite Shared: WorkgroupArray<f32,64>,")]
    [InlineData("@workgroup readwrite Shared: WorkgroupArray<float3,64>,")]
    [InlineData("@workgroup readwrite Shared: WorkgroupArray<f32,4096>, @workgroup readwrite Other: WorkgroupArray<f32,1>,")]
    public void SharedStorageRequiresItsBoundedEntryContract(string shared)
    {
        var module = Compile("Output[lane] = 0.0;", shared: shared);
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-WORKGROUP-0001");
    }

    [Theory]
    [InlineData("Exp(1.0)", true)]
    [InlineData("Sin(1.0)", true)]
    [InlineData("Cos(1.0)", true)]
    [InlineData("Pow(2.0, 3.0)", true)]
    [InlineData("Exp(1)", false)]
    [InlineData("Sin(1.0, 2.0)", false)]
    [InlineData("Cos()", false)]
    [InlineData("Pow(2.0)", false)]
    public void TranscendentalsHaveTypedDxcLowering(string expression, bool admitted)
    {
        var module = Compile("Output[lane] = " + expression + ";");
        Assert.Equal(admitted, module.Success);
        if (admitted)
        {
            var backend = VdMirComputeBackend.Compile(module);
            Assert.True(backend.SpirvValidated, backend.DxcOutput);
        }
        else
        {
            Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-CALL-0001");
        }
    }

    [Theory]
    [InlineData("lane")]
    [InlineData("group.x")]
    public void CooperativeDimensionsCannotDependOnInvocationOrGroupIdentity(string dimension)
    {
        string source = $$"""
            @compute @numthreads(32,1,1)
            function Main(@builtin(groupId) group: uint3, @builtin(groupIndex) lane: u32,
                @binding(0) readonly A: StorageBuffer<u32>, @binding(1) readonly B: StorageBuffer<u32>,
                @binding(2) readwrite C: StorageBuffer<f32>): void {
                CooperativeMatMul(A, B, C, group, lane, 16, 16, {{dimension}});
            }
            """;
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("cooperative.v.ts", source)]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-COOPERATIVE-0002");
    }

    [Fact]
    public void LiteralSharedIndicesAndOpaqueCopiesAreRejected()
    {
        var indexing = Compile("Output[lane] = Shared[64];");
        Assert.Contains(indexing.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-WORKGROUP-0001");
        var copying = Compile("let copy = Shared;");
        Assert.Contains(copying.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-STORAGE-0002");
    }

    [Theory]
    [InlineData("16", "group", "lane", "16", "COPE-GPU-WORKGROUP-0001")]
    [InlineData("32", "group", "lane + 1", "16", "COPE-GPU-COOPERATIVE-0001")]
    [InlineData("32", "group", "lane", "1.0", "COPE-GPU-COOPERATIVE-0001")]
    public void CooperativeCommandsRequireTheExactInvocationContract(string threads, string group, string lane,
        string inner, string diagnostic)
    {
        string source = $$"""
            @compute @numthreads({{threads}},1,1)
            function Main(@builtin(groupId) group: uint3, @builtin(groupIndex) lane: u32,
                @binding(0) readonly A: StorageBuffer<u32>, @binding(1) readonly B: StorageBuffer<u32>,
                @binding(2) readwrite C: StorageBuffer<f32>): void {
                CooperativeMatMul(A, B, C, {{group}}, {{lane}}, 16, 16, {{inner}});
            }
            """;
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("cooperative.v.ts", source)]));
        Assert.False(module.Success);
        Assert.Contains(module.Diagnostics, item => item.Code == diagnostic);
    }
}
