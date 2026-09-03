using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Rendering.Contracts.Shaders;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanNativeForwardTexturedContractM0Tests
{
    [Fact]
    public void Validate_RejectsMissingTextureBindingBeforeDraw()
    {
        (CompiledGraphicsProgram program, VulkanForwardTexturedFixture fixture) = CreateContract();

        VulkanForwardTexturedValidation result = VulkanNativeForwardTexturedRenderer.Validate(
            program,
            fixture with { BoundBindings = new HashSet<int> { 1, 2 } });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("missing required descriptor binding(s): 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsWrongMaterialSizeBeforeUpload()
    {
        (CompiledGraphicsProgram program, VulkanForwardTexturedFixture fixture) = CreateContract();

        VulkanForwardTexturedValidation result = VulkanNativeForwardTexturedRenderer.Validate(
            program,
            fixture with { MaterialBytes = new byte[31] });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("requires 32 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsIncompatibleVertexStrideBeforeDraw()
    {
        (CompiledGraphicsProgram program, VulkanForwardTexturedFixture fixture) = CreateContract();

        VulkanForwardTexturedValidation result = VulkanNativeForwardTexturedRenderer.Validate(
            program,
            fixture with { VertexStride = 24 });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("requires 20 bytes", StringComparison.Ordinal));
    }

    private static (CompiledGraphicsProgram Program, VulkanForwardTexturedFixture Fixture) CreateContract()
    {
        byte[] spirvHeader =
        [
            0x03, 0x02, 0x23, 0x07,
            0x00, 0x06, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        var shaders = new CompiledShaderProgram(
            CompiledShaderProgram.CurrentFormatVersion,
            [
                new CompiledShaderStage(CompiledShaderStageKind.Vertex, "VertexMain", "vs_6_0", spirvHeader, new string('0', 64), "fixture"),
                new CompiledShaderStage(CompiledShaderStageKind.Fragment, "PixelMain", "ps_6_0", spirvHeader, new string('1', 64), "fixture"),
            ]);
        var resources = new CompiledGraphicsResource[]
        {
            new(0, "albedo", CompiledGraphicsResourceKind.Texture2D, "Texture2D<float4>", 0, 0, [CompiledGraphicsStage.Fragment]),
            new(1, "linearSampler", CompiledGraphicsResourceKind.Sampler, "Sampler", 0, 1, [CompiledGraphicsStage.Fragment]),
            new(2, "material", CompiledGraphicsResourceKind.UniformBuffer, "SurfaceMaterial", 0, 2, [CompiledGraphicsStage.Fragment]),
        };
        var material = new CompiledMaterialLayout(
            "SurfaceMaterial",
            32,
            0,
            2,
            [CompiledGraphicsStage.Fragment],
            [
                new CompiledMaterialField(0, "tint", "float4", 0, 16, 16),
                new CompiledMaterialField(1, "roughness", "float", 16, 4, 4),
            ]);
        var program = new CompiledGraphicsProgram(
            CompiledGraphicsProgram.CurrentFormatVersion,
            "GraphicsProgram",
            "graphics.m3",
            "dxc-vulkan1.3",
            new string('2', 64),
            shaders,
            [
                new CompiledVertexInput(0, "position", "ObjectPosition3", "float3", 0, "object.position"),
                new CompiledVertexInput(1, "uv", "float2", "float2", 1, null),
            ],
            [new CompiledPixelTarget(0, "color", "float4", 0)],
            resources,
            material);
        var fixture = new VulkanForwardTexturedFixture(
            new byte[120],
            20,
            6,
            new byte[16],
            2,
            2,
            new byte[32],
            new HashSet<int> { 0, 1, 2 });
        return (program, fixture);
    }
}
