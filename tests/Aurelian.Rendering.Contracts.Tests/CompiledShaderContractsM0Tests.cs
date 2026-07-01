using System.Reflection;
using Aurelian.Rendering.Contracts.Shaders;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class CompiledShaderContractsM0Tests
{
    [Fact]
    public void CompiledShaderProgram_CanHoldVertexAndFragmentStages()
    {
        var program = new CompiledShaderProgram(
            CompiledShaderProgram.CurrentFormatVersion,
            [Stage(CompiledShaderStageKind.Vertex), Stage(CompiledShaderStageKind.Fragment)]);

        Assert.Equal("aurelian.compiled-shader-program/0", program.FormatVersion);
        Assert.Equal(2, program.Stages.Count);
        Assert.Contains(program.Stages, x => x.Stage == CompiledShaderStageKind.Vertex);
        Assert.Contains(program.Stages, x => x.Stage == CompiledShaderStageKind.Fragment);
    }

    [Fact]
    public void CompiledShaderStage_StoresSpirvBytesAndHash()
    {
        byte[] bytes = [0x03, 0x02, 0x23, 0x07];
        var stage = new CompiledShaderStage(
            CompiledShaderStageKind.Vertex,
            "VSMain",
            "vs_6_0",
            bytes,
            new string('a', 64),
            "shader.hlsl");

        Assert.Same(bytes, stage.SpirvBytes);
        Assert.Equal(new string('a', 64), stage.SpirvSha256);
        Assert.Equal("VSMain", stage.EntryPoint);
        Assert.Equal("vs_6_0", stage.Profile);
        Assert.Equal("shader.hlsl", stage.SourceName);
    }

    [Fact]
    public void CompiledShaderContracts_DoNotReferenceShadersOrGraphics()
    {
        Assembly assembly = typeof(CompiledShaderProgram).Assembly;

        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name == "Aurelian.Shaders");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name == "Aurelian.Graphics");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), x => x.Name is "Silk.NET.Vulkan" or "Microsoft.Direct3D.DXC");
    }

    private static CompiledShaderStage Stage(CompiledShaderStageKind kind)
        => new(kind, "main", kind == CompiledShaderStageKind.Vertex ? "vs_6_0" : "ps_6_0", [0x03, 0x02, 0x23, 0x07], new string('0', 64), "shader.hlsl");
}
