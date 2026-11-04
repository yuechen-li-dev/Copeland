using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuConstBindingsTests
{
    [Theory]
    [InlineData("const gain: f32 = value; return gain;", false)]
    [InlineData("let gain: f32 = value; return gain;", true)]
    [InlineData("var gain: f32 = value; gain = gain + 1.0; return gain;", true)]
    [InlineData("let gain: f32 = 2.0; const result: f32 = gain; return result;", false)]
    [InlineData("const gain: f32 = 2.0; const result: f32 = gain * 3.0; return result;", true)]
    public void LocalKeywordsDistinguishCompileTimeRuntimeAndMutableValues(string body, bool accepted)
    {
        foreach (var module in BindBoth("function Answer(value: f32): f32 { " + body + " }"))
        {
            Assert.Equal(accepted, module.Success);
            if (!accepted) Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-CONST-0001");
        }
    }

    [Fact]
    public void ConstantsReusePureStaticEvaluationAndFoldDependentNames()
    {
        const string source = """
            const GlobalGain: f32 = static (2.0);
            function Square(value: f32): f32 { return value * value; }
            function Answer(value: f32): f32 {
                const gain: f32 = Square(GlobalGain + 1.0);
                const result: f32 = gain + 1.0;
                return result;
            }
            """;
        foreach (var module in BindBoth(source))
        {
            Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
            var answer = Assert.Single(module.Functions, function => function.Name == "Answer");
            Assert.Equal("literal", answer.Statements[^1].Expression!.Kind);
            Assert.Equal("10.0", answer.Statements[^1].Expression!.Value);
            Assert.DoesNotContain(module.Functions, function => function.Name == "Square");
        }
    }

    [Fact]
    public void ClosedVectorConstantsAndFieldsUseTheExistingStaticEvaluator()
    {
        const string source = """
            const Tint: float3 = float3(1.0, 2.0, 3.0);
            function Answer(value: f32): f32 {
                const color: float4 = float4(Tint.x, Tint.y, Tint.z, 4.0);
                const selected: float2 = color.wx;
                const result: f32 = selected.x + selected.y;
                return result;
            }
            """;
        foreach (var module in BindBoth(source))
        {
            Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
            Assert.Equal("5.0", Assert.Single(module.Functions, function => function.Name == "Answer").Statements[^1].Expression!.Value);
        }
    }

    [Fact]
    public void BranchConstantsInheritOuterValuesWithoutLeakingBetweenArms()
    {
        const string source = """
            function Answer(value: f32): f32 {
                const outer: f32 = 2.0;
                if (value > 0.0) {
                    const branch: f32 = outer * 3.0;
                    return branch;
                } else {
                    const branch: f32 = outer * 4.0;
                    return branch;
                }
            }
            """;
        foreach (var module in BindBoth(source))
        {
            Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
            var conditional = Assert.Single(module.Functions, function => function.Name == "Answer").Statements[^1];
            Assert.Equal("6.0", conditional.Body![^1].Expression!.Value);
            Assert.Equal("8.0", conditional.ElseBody![^1].Expression!.Value);
        }
    }

    [Fact]
    public void ComputeBuiltinsAndStorageReadsCannotInitializeConstants()
    {
        foreach (string initializer in new[] { "thread.x", "U32(Input[thread.x])" })
        {
            var module = GpuComputeBinder.Compile(new([new("constant.v.ts", $$"""
                @compute @numthreads(1, 1, 1)
                function Main(@builtin(dispatchThreadId) thread: uint3,
                    @binding(0) readonly Input: StorageBuffer<f32>,
                    @binding(1) readwrite Output: StorageBuffer<f32>): void {
                    const index: u32 = {{initializer}};
                    Output[thread.x] = Convert<f32>(index);
                }
                """)]));
            Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-CONST-0001");
        }
    }

    private sealed record BoundResult(bool Success, IReadOnlyList<VdMirDiagnostic> Diagnostics, IReadOnlyList<VdMirFunction> Functions);

    private static IEnumerable<BoundResult> BindBoth(string source)
    {
        var graphics = GpuGraphicsBinder.Compile(new([new("constant.v.ts", GpuLanguagePortTests.Graphics + source)]));
        yield return new(graphics.Success, graphics.Diagnostics, graphics.Functions);
        var compute = GpuComputeBinder.Compile(new([new("constant.v.ts", source + """
            @compute @numthreads(1, 1, 1)
            function Main(@builtin(dispatchThreadId) thread: uint3,
                @binding(0) readonly Input: StorageBuffer<f32>,
                @binding(1) readwrite Output: StorageBuffer<f32>): void {
                Output[thread.x] = Answer(Input[thread.x]);
            }
            """)]));
        yield return new(compute.Success, compute.Diagnostics, compute.Functions);
    }
}
