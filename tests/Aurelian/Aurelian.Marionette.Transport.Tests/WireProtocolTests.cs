using System.Buffers.Binary;
using Xunit;

namespace Aurelian.Marionette.Transport.Tests;

public sealed class WireProtocolTests
{
    [Fact]
    public async Task Frame_RoundTrips()
    {
        var request = new TransportRequest(MarionetteWireProtocol.Version, "ping", "request-1", 1);
        using var stream = new MemoryStream(MarionetteWireProtocol.Encode(request));
        TransportRequest result = await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None);
        Assert.Equal(request, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public async Task Frame_RejectsInvalidLengths(uint length)
    {
        byte[] frame = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, length);
        using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_RejectsTruncatedPayload()
    {
        byte[] frame = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, 8);
        using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_RejectsMalformedJson()
    {
        byte[] payload = "{not-json}"u8.ToArray();
        byte[] frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        using var stream = new MemoryStream(frame);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await MarionetteWireProtocol.ReadAsync<TransportRequest>(stream, CancellationToken.None));
    }
}
