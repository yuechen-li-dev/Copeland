using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VdMirGraphicsBackendM3Tests
{
    [Fact]
    public void ForwardTextured_Emits_Texture_Sampler_Material_And_Typed_Sample()
    {
        VdMirGraphicsModule module = Compile();
        string hlsl = VdMirGraphicsHlslEmitter.Emit(module);

        Assert.Contains("[[vk::binding(0, 0)]] Texture2D<float4> albedo;", hlsl, StringComparison.Ordinal);
        Assert.Contains("[[vk::binding(1, 0)]] SamplerState linearSampler;", hlsl, StringComparison.Ordinal);
        Assert.Contains("[[vk::binding(2, 0)]] ConstantBuffer<SurfaceMaterial> material;", hlsl, StringComparison.Ordinal);
        Assert.Contains("float4 tint; // offset 0, size 16, align 16", hlsl, StringComparison.Ordinal);
        Assert.Contains("float roughness; // offset 16, size 4, align 4", hlsl, StringComparison.Ordinal);
        Assert.Contains("albedo.Sample(linearSampler, input.uv)", hlsl, StringComparison.Ordinal);
        Assert.DoesNotContain("ForwardResources resources", hlsl, StringComparison.Ordinal);
    }

    [Fact]
    public void ForwardTextured_Compiles_And_Validates_Both_Spirv_Stages_With_Descriptor_Facts()
    {
        VdMirGraphicsBackendResult result = VdMirGraphicsBackend.Compile(Compile());

        Assert.True(result.Vertex.SpirvValidated, result.Vertex.SpirvValidationOutput + Environment.NewLine + result.Vertex.DxcOutput);
        Assert.True(result.Pixel.SpirvValidated, result.Pixel.SpirvValidationOutput + Environment.NewLine + result.Pixel.DxcOutput);
        Assert.Contains("OpEntryPoint Vertex", result.Vertex.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("OpEntryPoint Fragment", result.Pixel.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("BuiltIn VertexIndex", result.Vertex.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("BuiltIn InstanceIndex", result.Vertex.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("BuiltIn FrontFacing", result.Pixel.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("Binding 0", result.Pixel.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("Binding 1", result.Pixel.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("Binding 2", result.Pixel.SpirvDisassembly, StringComparison.Ordinal);
        Assert.Contains("DescriptorSet 0", result.Pixel.SpirvDisassembly, StringComparison.Ordinal);
    }

    [Fact]
    public void ForwardTextured_Backend_Is_Deterministic_And_Metadata_Is_Renderer_Facing()
    {
        VdMirGraphicsModule module = Compile();
        VdMirGraphicsBackendResult first = VdMirGraphicsBackend.Compile(module);
        VdMirGraphicsBackendResult second = VdMirGraphicsBackend.Compile(module);

        Assert.Equal(first.HlslSha256, second.HlslSha256);
        Assert.Equal(first.Vertex.SpirvSha256, second.Vertex.SpirvSha256);
        Assert.Equal(first.Pixel.SpirvSha256, second.Pixel.SpirvSha256);
        Assert.Equal(2, module.GraphicsProgram!.VertexInputs.Count);
        Assert.Equal(2, module.GraphicsProgram.Varyings.Count);
        Assert.Single(module.GraphicsProgram.PixelTargets);
        Assert.Equal(3, module.GraphicsProgram.Resources.Count);
        Assert.NotNull(module.GraphicsProgram.Material);
    }

    private static VdMirGraphicsModule Compile()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "samples", "Aurelian", "ForwardTexturedM3.v.ts")).Replace("\r\n", "\n", StringComparison.Ordinal);
        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("samples/Aurelian/ForwardTexturedM3.v.ts", source)]));
        Assert.True(module.Success, string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return module;
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}
