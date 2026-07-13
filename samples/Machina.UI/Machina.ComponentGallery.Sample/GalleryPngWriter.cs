using System.IO.Compression;
using Machina.Fonts.ReferenceRendering;

namespace Machina.ComponentGallery.Sample;

internal static class GalleryPngWriter
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrType = "IHDR"u8.ToArray();
    private static readonly byte[] IdatType = "IDAT"u8.ToArray();
    private static readonly byte[] IendType = "IEND"u8.ToArray();

    public static void Write(string outputPath, RasterFrame frame)
    {
        Write(outputPath, frame.Width, frame.Height, ToRgbaBytes(frame));
    }

    public static void Write(string outputPath, RgbaImage image)
    {
        Write(outputPath, image.Width, image.Height, ToRgbaBytes(image));
    }

    private static void Write(string outputPath, int width, int height, byte[] rgbaBytes)
    {
        using var stream = File.Create(outputPath);

        stream.Write(PngSignature, 0, PngSignature.Length);
        WriteChunk(stream, IhdrType, BuildHeader(width, height));
        WriteChunk(stream, IdatType, CompressImageData(width, height, rgbaBytes));
        WriteChunk(stream, IendType, []);
    }

    private static byte[] BuildHeader(int width, int height)
    {
        var header = new byte[13];
        WriteInt32BigEndian(header, 0, width);
        WriteInt32BigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        return header;
    }

    private static byte[] CompressImageData(int width, int height, byte[] rgbaBytes)
    {
        var rowLength = (width * 4) + 1;
        var rawImageData = new byte[rowLength * height];
        var sourceIndex = 0;
        var targetIndex = 0;

        for (var y = 0; y < height; y++)
        {
            rawImageData[targetIndex++] = 0;

            Buffer.BlockCopy(rgbaBytes, sourceIndex, rawImageData, targetIndex, width * 4);
            sourceIndex += width * 4;
            targetIndex += width * 4;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write(rawImageData, 0, rawImageData.Length);
        }

        return compressed.ToArray();
    }

    private static byte[] ToRgbaBytes(RasterFrame frame)
    {
        var bytes = new byte[frame.Width * frame.Height * 4];
        var index = 0;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = frame.Surface.GetPixel(x, y);
                bytes[index++] = pixel.R;
                bytes[index++] = pixel.G;
                bytes[index++] = pixel.B;
                bytes[index++] = pixel.A;
            }
        }

        return bytes;
    }

    private static byte[] ToRgbaBytes(RgbaImage image)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        var index = 0;

        for (var i = 0; i < image.Pixels.Length; i++)
        {
            var pixel = image.Pixels[i];
            bytes[index++] = pixel.R;
            bytes[index++] = pixel.G;
            bytes[index++] = pixel.B;
            bytes[index++] = pixel.A;
        }

        return bytes;
    }

    private static void WriteChunk(Stream stream, byte[] chunkType, byte[] data)
    {
        WriteInt32BigEndian(stream, data.Length);
        stream.Write(chunkType, 0, chunkType.Length);
        stream.Write(data, 0, data.Length);

        var crc = Crc32.Compute(chunkType, data);
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
            var crc = 0xFFFFFFFFu;
            crc = Update(crc, chunkType);
            crc = Update(crc, data);
            return ~crc;
        }

        private static uint Update(uint crc, byte[] bytes)
        {
            for (var index = 0; index < bytes.Length; index++)
            {
                crc = Table[(crc ^ bytes[index]) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];

            for (uint index = 0; index < table.Length; index++)
            {
                var value = index;

                for (var bit = 0; bit < 8; bit++)
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
