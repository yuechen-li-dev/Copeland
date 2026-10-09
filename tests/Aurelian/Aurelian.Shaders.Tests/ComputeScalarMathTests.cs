using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ComputeScalarMathTests
{
    [Theory]
    [InlineData("Clamp(Max(Abs(-Input[thread.x]), 0.0), 0.0, 1.0)", true)]
    [InlineData("Abs(1)", false)]
    [InlineData("Min(1.0)", false)]
    [InlineData("Max(1.0, 2.0, 3.0)", false)]
    [InlineData("Clamp(1.0, 0.0)", false)]
    [InlineData("-thread.x", false)]
    public void Math_admission_checks_types_and_arity(string expression, bool admitted)
    {
        string source = $$"""
            @compute
            @numthreads(1, 1, 1)
            function Main(@builtin(dispatchThreadId) thread: uint3,
                @binding(0) readonly Input: StorageBuffer<f32>,
                @binding(1) readwrite Output: StorageBuffer<f32>): void {
                Output[thread.x] = {{expression}};
            }
            """;
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("math.v.ts", source)]));
        Assert.Equal(admitted, module.Success);
        if (admitted)
        {
            string hlsl = VdMirComputeHlslEmitter.Emit(module);
            Assert.Contains("clamp(max(abs((-Input[thread.x]))", hlsl);
        }
        else
        {
            Assert.Contains(module.Diagnostics, item => item.Code is "COPE-GPU-CALL-0001" or "COPE-GPU-OPERATOR-0001");
        }
    }

    [Theory]
    [InlineData("!(Input[thread.x] < 0.0) && Input[thread.x] < 1.0", true)]
    [InlineData("true || false", true)]
    [InlineData("true == false", true)]
    [InlineData("1.0 && 2.0", false)]
    public void Boolean_operators_require_boolean_operands(string condition, bool admitted)
    {
        string source = $$"""
            @compute
            @numthreads(1, 1, 1)
            function Main(@builtin(dispatchThreadId) thread: uint3,
                @binding(0) readonly Input: StorageBuffer<f32>,
                @binding(1) readwrite Output: StorageBuffer<f32>): void {
                if ({{condition}}) {
                    Output[thread.x] = 1.0;
                }
            }
            """;
        var module = GpuComputeBinder.Compile(new([new GpuSourceFile("boolean.v.ts", source)]));
        Assert.Equal(admitted, module.Success);
        if (admitted)
        {
            Assert.Contains("if (", VdMirComputeHlslEmitter.Emit(module));
        }
        else
        {
            Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-OPERATOR-0001");
        }
    }
}
