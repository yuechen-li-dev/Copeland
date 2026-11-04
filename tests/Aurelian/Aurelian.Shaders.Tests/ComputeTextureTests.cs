using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ComputeTextureTests
{
    private const string Source = """
        function ReadColor(image: Texture2D<float4>, filter: Sampler, uv: float2): float4 {
            return SampleLevel(image, filter, uv, 0.0);
        }
        @compute @numthreads(64, 1, 1)
        function Main(@builtin(dispatchThreadId) thread: uint3,
            @binding(0) readonly Images: DescriptorArray<Texture2D<float4>, 4>,
            @binding(1) readonly Filters: DescriptorArray<Sampler, 4>,
            @binding(2) readwrite Output: StorageBuffer<f32>): void {
            let index: u32 = thread.x % 4;
            let color: float4 = ReadColor(Images[NonUniform(index)], Filters[NonUniform(index)], float2(0.3, 0.7));
            Output[thread.x] = color.x;
        }
        """;

    [Fact]
    public void ExplicitLodAndNonuniformSampledDescriptorsCompileToValidatedSpirv()
    {
        var module = GpuComputeBinder.Compile(new([new("sampled.v.ts", Source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        Assert.Equal("compute.sampled.m1", module.FeatureLevel);
        Assert.Equal(4, module.Resources[0].DescriptorCount);
        var compiled = VdMirComputeBackend.Compile(module);
        Assert.True(compiled.SpirvValidated, compiled.DxcOutput + compiled.SpirvValidationOutput);
        Assert.Contains("OpImageSampleExplicitLod", compiled.SpirvDisassembly);
        Assert.Contains("NonUniform", compiled.SpirvDisassembly);
    }

    [Fact]
    public void OpaqueResourcesCannotEscapeThroughReturnsOrDescriptorArrayHelpers()
    {
        string returning = "function Escape(filter: Sampler): Sampler { return filter; }\n" + Source;
        returning = returning.Replace("Filters[NonUniform(index)]", "Escape(Filters[NonUniform(index)])", StringComparison.Ordinal);
        var module = GpuComputeBinder.Compile(new([new("return.v.ts", returning)]));
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-STORAGE-0002");

        string arrays = "function Select(filters: DescriptorArray<Sampler, 4>): Sampler { return filters[0]; }\n" + Source;
        arrays = arrays.Replace("Filters[NonUniform(index)]", "Select(Filters)", StringComparison.Ordinal);
        module = GpuComputeBinder.Compile(new([new("array.v.ts", arrays)]));
        Assert.Contains(module.Diagnostics, item => item.Code == "COPE-GPU-TABLE-0001");
    }

    [Theory]
    [InlineData("Images[NonUniform(index)]", "Images[index]", "COPE-GPU-TABLE-0003")]
    [InlineData("Images[NonUniform(index)]", "Images[4]", "COPE-GPU-TABLE-0004")]
    [InlineData("return SampleLevel(image, filter, uv, 0.0);", "return Sample(image, filter, uv);", "COPE-GPU-SAMPLE-0001")]
    [InlineData("float2(0.3, 0.7)", "float3(0.3, 0.7, 0.1)", "COPE-GPU-TYPE-0002")]
    [InlineData("return SampleLevel(image, filter, uv, 0.0);", "return SampleLevel(filter, image, uv, 0.0);", "COPE-GPU-SAMPLE-0001")]
    [InlineData("let color: float4 =", "let copy: Sampler = Filters[0]; let color: float4 =", "COPE-GPU-STORAGE-0002")]
    [InlineData("readonly Images", "readwrite Images", "COPE-GPU-RESOURCE-0001")]
    public void InvalidDescriptorAndImplicitDerivativeContractsAreRejected(string before, string after, string code)
    {
        var module = GpuComputeBinder.Compile(new([new("invalid.v.ts", Source.Replace(before, after, StringComparison.Ordinal))]));
        Assert.Contains(module.Diagnostics, item => item.Code == code);
    }
}
