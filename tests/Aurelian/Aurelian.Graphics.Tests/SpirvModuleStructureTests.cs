using System.Buffers.Binary;
using Aurelian.Graphics.Tests.Fixtures.Spirv;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class SpirvModuleStructureTests
{
    [Fact]
    public void RealShaderFixturesPassAndEveryTruncatedHeaderFails()
    {
        uint[] words = Words(TriangleSpirvFixtures.VertexBytes);
        Assert.True(SpirvModuleStructure.IsWellFormed(words));
        Assert.True(SpirvModuleStructure.IsWellFormed(Words(TriangleSpirvFixtures.FragmentBytes)));
        for (int count = 0; count <= 5; count++)
        {
            Assert.False(SpirvModuleStructure.IsWellFormed(words[..count]));
        }
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 0x00010001u)]
    [InlineData(3, 0u)]
    [InlineData(4, 1u)]
    [InlineData(5, 0u)]
    [InlineData(5, 0xffff0000u)]
    public void InvalidHeadersAndInstructionLengthsFailBeforeNativeCalls(int index, uint value)
    {
        uint[] words = Words(TriangleSpirvFixtures.VertexBytes);
        words[index] = value;
        Assert.False(SpirvModuleStructure.IsWellFormed(words));
    }

    private static uint[] Words(byte[] bytes)
    {
        var words = new uint[bytes.Length / sizeof(uint)];
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)));
        }
        return words;
    }
}
