using System.IO.Compression;

namespace TinyFarm.Native;

internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string path, int width, int height, byte[] rgba)
    {
        using var stream = File.Create(path);
        stream.Write(Signature);
        WriteChunk(stream, "IHDR"u8, Header(width, height));
        WriteChunk(stream, "sRGB"u8, [0]);
        WriteChunk(stream, "gAMA"u8, [0, 0, 177, 143]);
        WriteChunk(stream, "IDAT"u8, Compress(width, height, rgba));
        WriteChunk(stream, "IEND"u8, []);
    }

    private static byte[] Header(int width, int height)
    {
        var data = new byte[13];
        WriteBigEndian(data, 0, width);
        WriteBigEndian(data, 4, height);
        data[8] = 8;
        data[9] = 6;
        return data;
    }

    private static byte[] Compress(int width, int height, byte[] rgba)
    {
        int rowBytes = width * 4;
        var raw = new byte[(rowBytes + 1) * height];
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(rgba, row * rowBytes, raw, row * (rowBytes + 1) + 1, rowBytes);
        }
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, byte[] data)
    {
        WriteBigEndian(stream, data.Length);
        stream.Write(type);
        stream.Write(data);
        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        WriteBigEndian(stream, unchecked((int)~crc));
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return crc;
    }

    private static void WriteBigEndian(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        WriteBigEndian(bytes, 0, value);
        stream.Write(bytes);
    }

    private static void WriteBigEndian(Span<byte> bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
