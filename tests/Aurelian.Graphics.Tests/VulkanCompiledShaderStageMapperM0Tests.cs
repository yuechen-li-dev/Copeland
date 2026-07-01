using System.Reflection;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Rendering.Contracts.Shaders;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanCompiledShaderStageMapperM0Tests
{
    [Fact]
    public void VulkanCompiledShaderStageMapper_MapsVertexAndFragmentStages()
    {
        VulkanCompiledShaderStageMappingResult result = VulkanCompiledShaderStageMapper.ToVulkanShaderStages(
            Program(Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)));

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.Collection(
            result.Stages,
            vertex =>
            {
                Assert.Equal(VulkanShaderStageKind.Vertex, vertex.Stage);
                Assert.Equal("main", vertex.EntryPoint);
            },
            fragment => Assert.Equal(VulkanShaderStageKind.Fragment, fragment.Stage));
    }

    [Fact]
    public void VulkanCompiledShaderStageMapper_RejectsComputeForGraphicsPipeline()
    {
        VulkanCompiledShaderStageMappingResult result = VulkanCompiledShaderStageMapper.ToVulkanShaderStages(
            Program(Stage(CompiledShaderStageKind.Compute)));

        Assert.False(result.Success);
        Assert.Empty(result.Stages);
        Assert.Contains(result.Diagnostics, x => x.Code == VulkanCompiledShaderStageMappingDiagnosticCodes.UnsupportedComputeStage);
    }

    [Fact]
    public void VulkanCompiledShaderStageMapper_RejectsSpirvByteLengthNotMultipleOfFour()
    {
        VulkanCompiledShaderStageMappingResult result = VulkanCompiledShaderStageMapper.ToVulkanShaderStages(
            Program(Stage(CompiledShaderStageKind.Vertex, [0x03, 0x02, 0x23])));

        Assert.False(result.Success);
        Assert.Empty(result.Stages);
        Assert.Contains(result.Diagnostics, x => x.Code == VulkanCompiledShaderStageMappingDiagnosticCodes.InvalidSpirvByteLength);
    }

    [Fact]
    public void VulkanCompiledShaderStageMapper_RejectsInvalidSpirvMagic()
    {
        VulkanCompiledShaderStageMappingResult result = VulkanCompiledShaderStageMapper.ToVulkanShaderStages(
            Program(Stage(CompiledShaderStageKind.Vertex, [0x00, 0x00, 0x00, 0x00])));

        Assert.False(result.Success);
        Assert.Empty(result.Stages);
        Assert.Contains(result.Diagnostics, x => x.Code == VulkanCompiledShaderStageMappingDiagnosticCodes.InvalidSpirvMagic);
    }

    [Fact]
    public void VulkanCompiledShaderStageMapper_ConvertsLittleEndianBytesToWords()
    {
        VulkanCompiledShaderStageMappingResult result = VulkanCompiledShaderStageMapper.ToVulkanShaderStages(
            Program(Stage(CompiledShaderStageKind.Vertex, [0x03, 0x02, 0x23, 0x07, 0x04, 0x03, 0x02, 0x01])));

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.Equal([0x07230203u, 0x01020304u], result.Stages.Single().SpirvWords);
    }

    [Fact]
    public void VulkanCompiledShaderStageMapper_DoesNotReferenceAurelianShaders()
    {
        Assembly assembly = typeof(VulkanCompiledShaderStageMapper).Assembly;

        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name == "Aurelian.Shaders");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name is "Microsoft.Direct3D.DXC");
    }

    private static CompiledShaderProgram Program(params CompiledShaderStage[] stages)
        => new(CompiledShaderProgram.CurrentFormatVersion, stages);

    private static CompiledShaderStage Stage(CompiledShaderStageKind stage, byte[]? spirvBytes = null)
        => new(stage, "main", Profile(stage), spirvBytes ?? ValidSpirvBytes(), new string('f', 64), "shader.hlsl");

    private static string Profile(CompiledShaderStageKind stage)
        => stage switch
        {
            CompiledShaderStageKind.Vertex => "vs_6_0",
            CompiledShaderStageKind.Fragment => "ps_6_0",
            CompiledShaderStageKind.Compute => "cs_6_0",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported stage."),
        };

    private static byte[] ValidSpirvBytes()
        => [0x03, 0x02, 0x23, 0x07, 0x00, 0x00, 0x01, 0x00];

    private static string FormatDiagnostics(IReadOnlyList<VulkanCompiledShaderStageMappingDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Code}: {x.Message}"));
}
