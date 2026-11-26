using System.Buffers.Binary;
using Aurelian.Inference;
using Xunit;

namespace Aurelian.Inference.Tests;

public sealed class GgufBlocksTests
{
    [Fact]
    public void Q4RepackingPreservesSignedScaleAndNibblePlanes()
    {
        var bytes = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, BitConverter.HalfToUInt16Bits((Half)(-0.25f)));
        for (int index = 0; index < 16; index++)
        {
            bytes[index + 2] = (byte)(index | (15 - index) << 4);
        }
        var words = new uint[5];
        GgufBlocks.RepackQ4_0(bytes, words);
        for (int index = 0; index < 16; index++)
        {
            Assert.Equal((index - 8) * -0.25f, BlockQ4.Decode(words, index));
            Assert.Equal((7 - index) * -0.25f, BlockQ4.Decode(words, index + 16));
        }
    }

    [Fact]
    public void InterleavedQ4PreservesOriginalScalesAndNibblesAcrossGroups()
    {
        const int rows = 16;
        const int blocksPerRow = 2;
        var bytes = new byte[rows * blocksPerRow * 18];
        for (int block = 0; block < rows * blocksPerRow; block++)
        {
            Span<byte> source = bytes.AsSpan(block * 18, 18);
            Half scale = (Half)((block - 11) / 32f);
            BinaryPrimitives.WriteUInt16LittleEndian(source, BitConverter.HalfToUInt16Bits(scale));
            for (int index = 0; index < 16; index++)
            {
                source[index + 2] = (byte)((index + 13 * block) & 255);
            }
        }
        var reference = new uint[rows * blocksPerRow * 5];
        var interleaved = new uint[rows * blocksPerRow / 8 * 36];
        GgufBlocks.RepackQ4_0(bytes, reference);
        GgufBlocks.RepackQ4_0Interleaved8(bytes, interleaved, blocksPerRow * 32);
        for (int row = 0; row < rows; row++)
        {
            for (int block = 0; block < blocksPerRow; block++)
            {
                int start = (row / 8 * blocksPerRow + block) * 36;
                int source = (row * blocksPerRow + block) * 5;
                int lane = row % 8;
                uint pair = interleaved[start + lane / 2];
                float scale = (float)BitConverter.UInt16BitsToHalf((ushort)(pair >> (lane % 2 * 16)));
                Assert.Equal(reference[source], BitConverter.SingleToUInt32Bits(scale));
                for (int plane = 0; plane < 4; plane++)
                {
                    Assert.Equal(reference[source + plane + 1], interleaved[start + 4 + plane * 8 + lane]);
                }
            }
        }
    }

    [Fact]
    public void Q6DecodesHighBitsAndSignedSixteenValueScales()
    {
        var bytes = new byte[210];
        bytes.AsSpan(0, 128).Fill(0x37);
        bytes.AsSpan(128, 64).Fill(0xe4);
        for (int group = 0; group < 16; group++)
        {
            bytes[192 + group] = (byte)(249 + group % 7);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(208), BitConverter.HalfToUInt16Bits((Half)0.5f));
        var output = new float[256];
        GgufBlocks.DecodeQ6_K(bytes, output);
        int[] codes = [7, 23, 35, 51];
        for (int index = 0; index < output.Length; index++)
        {
            Assert.Equal((codes[index / 32 % 4] - 32) * (-7 + index / 16 % 7) * 0.5f, output[index]);
        }
    }
}
