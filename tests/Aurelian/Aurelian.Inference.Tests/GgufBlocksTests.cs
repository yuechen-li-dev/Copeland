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
