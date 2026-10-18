using System.IO.Compression;

namespace Machina.Fonts.ReferenceRendering;

/// <summary>
/// Writes RGBA images as non-interlaced PNG files using unfiltered rows.
/// </summary>
internal static class RgbaPngWriter
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrType = "IHDR"u8.ToArray();
    private static readonly byte[] IdatType = "IDAT"u8.ToArray();
    private static readonly byte[] IendType = "IEND"u8.ToArray();

    /// <summary>
    /// Writes the supplied image to <paramref name="outputPath"/>.
    /// </summary>
    public static void Write(string outputPath, RgbaImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(image);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(outputPath);
        stream.Write(PngSignature, 0, PngSignature.Length);
        WriteChunk(stream, IhdrType, BuildHeader(image.Width, image.Height));
        WriteChunk(stream, IdatType, CompressImageData(image));
        WriteChunk(stream, IendType, []);
    }

    private static byte[] BuildHeader(int width, int height)
    {
        byte[] header = new byte[13];
        WriteInt32BigEndian(header, 0, width);
        WriteInt32BigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 6;
        return header;
    }

    private static byte[] CompressImageData(RgbaImage image)
    {
        int rowLength = checked((image.Width * 4) + 1);
        byte[] rawImageData = new byte[checked(rowLength * image.Height)];
        int targetIndex = 0;
        int sourceIndex = 0;

        for (int y = 0; y < image.Height; y++)
        {
            rawImageData[targetIndex++] = 0;

            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image.Pixels[sourceIndex++];
                rawImageData[targetIndex++] = pixel.R;
                rawImageData[targetIndex++] = pixel.G;
                rawImageData[targetIndex++] = pixel.B;
                rawImageData[targetIndex++] = pixel.A;
            }
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write(rawImageData, 0, rawImageData.Length);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, byte[] chunkType, byte[] data)
    {
        WriteInt32BigEndian(stream, data.Length);
        stream.Write(chunkType, 0, chunkType.Length);
        stream.Write(data, 0, data.Length);

        uint crc = Crc32.Compute(chunkType, data);
        WriteInt32BigEndian(stream, unchecked((int)crc));
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        WriteInt32BigEndian(buffer, 0, value);
        stream.Write(buffer);
    }

    private static void WriteInt32BigEndian(Span<byte> buffer, int offset, int value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(byte[] chunkType, byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            crc = Update(crc, chunkType);
            crc = Update(crc, data);
            return ~crc;
        }

        private static uint Update(uint crc, byte[] bytes)
        {
            for (int index = 0; index < bytes.Length; index++)
            {
                crc = Table[(crc ^ bytes[index]) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];

            for (uint index = 0; index < table.Length; index++)
            {
                uint value = index;

                for (int bit = 0; bit < 8; bit++)
                {
                    if ((value & 1) != 0)
                    {
                        value = 0xEDB88320u ^ (value >> 1);
                    }
                    else
                    {
                        value >>= 1;
                    }
                }

                table[index] = value;
            }

            return table;
        }
    }
}
