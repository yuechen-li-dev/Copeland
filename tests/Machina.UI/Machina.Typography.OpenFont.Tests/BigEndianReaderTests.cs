using Typography.OpenFont;
using Xunit;

namespace Machina.Typography.OpenFont.Tests;

public sealed class BigEndianReaderTests
{
    [Fact]
    public void FragmentedReadsStillConsumeAnEntireField()
    {
        using var stream = new FragmentedStream([0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0]);
        using var reader = new ByteOrderSwappingBinaryReader(stream);

        Assert.Equal(0x123456789ABCDEF0UL, reader.ReadUInt64());
        Assert.Equal(8, stream.Position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TruncatedReadCannotReuseBytesFromThePreviousField(int remaining)
    {
        byte[] bytes = [0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88, .. new byte[remaining]];
        using var stream = new MemoryStream(bytes);
        using var reader = new ByteOrderSwappingBinaryReader(stream);
        Assert.Equal(0xFFEEDDCCBBAA9988UL, reader.ReadUInt64());

        Assert.Throws<EndOfStreamException>(() => reader.ReadUInt32());
    }

    [Fact]
    public void MixedWidthFieldsPreserveBigEndianValuesAndCursor()
    {
        using var stream = new MemoryStream([0x80, 0x01, 0xFE, 0xDC, 0x89, 0xAB, 0xCD, 0xEF, 0x80, 0, 0, 0]);
        using var reader = new ByteOrderSwappingBinaryReader(stream);

        Assert.Equal(unchecked((short)0x8001), reader.ReadInt16());
        Assert.Equal((ushort)0xFEDC, reader.ReadUInt16());
        Assert.Equal(0x89ABCDEFU, reader.ReadUInt32());
        Assert.Equal(int.MinValue, reader.ReadInt32());
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void DoubleReadsUseTheOriginalIeeeBits()
    {
        using var stream = new FragmentedStream([0xC0, 0x29, 0, 0, 0, 0, 0, 0]);
        using var reader = new ByteOrderSwappingBinaryReader(stream);

        Assert.Equal(-12.5, reader.ReadDouble());
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(1, count));
    }
}
