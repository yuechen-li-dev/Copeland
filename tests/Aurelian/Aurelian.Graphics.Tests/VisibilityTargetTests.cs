using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Shaders;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VisibilityTargetTests
{
    [Theory]
    [InlineData("uint2", VulkanTextureFormat.Rg32UInt, true)]
    [InlineData("u32", VulkanTextureFormat.R32UInt, true)]
    [InlineData("float4", VulkanTextureFormat.Rgba16Float, true)]
    [InlineData("float4", VulkanTextureFormat.Rg32UInt, false)]
    [InlineData("uint2", VulkanTextureFormat.Rgba16Float, false)]
    [InlineData("uint2", VulkanTextureFormat.R32UInt, false)]
    public void NativeAttachmentScalarTypesMustMatchShaderTargets(string type, VulkanTextureFormat format, bool valid)
    {
        var shaders = new CompiledShaderProgram(CompiledShaderProgram.CurrentFormatVersion, []);
        var program = new CompiledGraphicsProgram(CompiledGraphicsProgram.CurrentFormatVersion, "typed-target",
            "graphics.visibility.m1", "copeland", "", shaders, [], [new(0, "identity", type, 0)], [], null);
        if (valid)
            VulkanVisibilityBuffer3D.ValidateTargets(program, [format]);
        else
            Assert.Contains("AUR-VBUFFER-002", Assert.Throws<ArgumentException>(() =>
                VulkanVisibilityBuffer3D.ValidateTargets(program, [format])).Message);
    }
}
