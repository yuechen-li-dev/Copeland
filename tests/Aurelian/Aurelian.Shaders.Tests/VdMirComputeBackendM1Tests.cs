using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Language.External.Dxc;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VdMirComputeBackendM1Tests
{
    private const string Source = """
        @compute
        @numthreads(8, 1, 1)
        function ComputeNoRegression_CS(
            @builtin(dispatchThreadId) thread: uint3,
            @binding(0) readonly Input: StorageBuffer<f32>,
            @binding(1) readwrite Output: StorageBuffer<f32>
        ): void {
            const index: u32 = thread.x;
            Output[index] = Input[index] + 1.0;
            return;
        }
        """;

    [Fact]
    public void Canonical_Compute_VdMir_Emits_Backend_Only_Hlsl_Spellings()
    {
        VdMirComputeModule module = Compile();

        string hlsl = VdMirComputeHlslEmitter.Emit(module);

        Assert.Contains("[[vk::binding(0, 0)]] StructuredBuffer<float> Input;", hlsl);
        Assert.Contains("[[vk::binding(1, 0)]] RWStructuredBuffer<float> Output;", hlsl);
        Assert.Contains("[numthreads(8, 1, 1)]", hlsl);
        Assert.Contains("uint3 thread : SV_DispatchThreadID", hlsl);
        Assert.Contains("Output[index] = (Input[index] + 1.0);", hlsl);
        Assert.DoesNotContain("SV_DispatchThreadID", Source);
        Assert.DoesNotContain("RWStructuredBuffer", Source);
    }

    [Fact]
    public void Backend_Compiles_Validates_Reflects_And_Repeats_Deterministically()
    {
        if (!DxcExecutableResolver.Resolve().Success)
        {
            return;
        }

        VdMirComputeBackendResult first = VdMirComputeBackend.Compile(Compile());
        VdMirComputeBackendResult second = VdMirComputeBackend.Compile(Compile());

        Assert.Equal(DxcSpirvStatus.Compiled, first.DxcStatus);
        Assert.True(first.SpirvValidated, first.SpirvValidationOutput);
        Assert.NotEmpty(first.Spirv);
        Assert.Equal(first.HlslSha256, second.HlslSha256);
        Assert.Equal(first.SpirvSha256, second.SpirvSha256);
        Assert.Contains("OpEntryPoint GLCompute", first.SpirvDisassembly);
        Assert.Contains("OpExecutionMode", first.SpirvDisassembly);
        Assert.Contains("LocalSize 8 1 1", first.SpirvDisassembly);
        Assert.Contains("Binding 0", first.SpirvDisassembly);
        Assert.Contains("Binding 1", first.SpirvDisassembly);
        Assert.Contains("DescriptorSet 0", first.SpirvDisassembly);
        Assert.Contains("cs_6_0", first.DxcArguments);
        Assert.Contains("-spirv", first.DxcArguments);
        Assert.Contains("-fspv-target-env=vulkan1.3", first.DxcArguments);
    }

    [Fact]
    public void Legacy_Sdslv_Frontend_Is_Not_On_The_Compute_Backend_Path()
    {
        string[] referencedTypes = typeof(VdMirComputeBackend).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Aurelian.Shaders.Compute")
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? string.Empty))
            .ToArray();

        Assert.DoesNotContain(referencedTypes, type => type.Contains("SdslvParser", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedTypes, type => type.Contains("SdslvValidator", StringComparison.Ordinal));
    }

    private static VdMirComputeModule Compile()
    {
        VdMirComputeModule module = GpuComputeBinder.Compile(new GpuCompilationRequest([
            new GpuSourceFile("ComputeNoRegression.v.ts", Source),
        ]));
        Assert.True(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return module;
    }
}
