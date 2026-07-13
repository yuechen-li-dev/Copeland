using System.IO.Compression;
using System.Buffers.Binary;
using Machina.Fonts.ReferenceRendering;

namespace Machina.Presenter.Sample;

internal static class PresenterPngWriter
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string outputPath, RasterFrame frame)
    {
        RgbaImage image = new(frame.Width, frame.Height);

        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                var pixel = frame.Surface.GetPixel(x, y);
                image.SetPixel(x, y, new Rgba32(pixel.R, pixel.G, pixel.B, pixel.A));
            }
        }

        Write(outputPath, image);
    }

    public static void Write(string outputPath, RgbaImage image)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using FileStream stream = File.Create(outputPath);
        stream.Write(PngSignature, 0, PngSignature.Length);

        WriteChunk(stream, "IHDR", BuildHeader(image.Width, image.Height));
        WriteChunk(stream, "IDAT", CompressImageData(image));
        WriteChunk(stream, "IEND", []);
    }

    private static byte[] BuildHeader(int width, int height)
    {
        byte[] data = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), (uint)height);
        data[8] = 8;
        data[9] = 6;
        data[10] = 0;
        data[11] = 0;
        data[12] = 0;
        return data;
    }

    private static byte[] CompressImageData(RgbaImage image)
    {
        byte[] scanlines = new byte[(image.Width * 4 + 1) * image.Height];
        byte[] rgba = ToRgbaBytes(image);
        int scanlineStride = image.Width * 4 + 1;

        for (int row = 0; row < image.Height; row++)
        {
            int offset = row * scanlineStride;
            scanlines[offset] = 0;
            Buffer.BlockCopy(rgba, row * image.Width * 4, scanlines, offset + 1, image.Width * 4);
        }

        using MemoryStream output = new();
        using (DeflateStream deflate = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(scanlines, 0, scanlines.Length);
        }

        return output.ToArray();
    }

    private static byte[] ToRgbaBytes(RgbaImage image)
    {
        byte[] bytes = new byte[image.Width * image.Height * 4];
        int index = 0;

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image.GetPixel(x, y);
                bytes[index++] = pixel.R;
                bytes[index++] = pixel.G;
                bytes[index++] = pixel.B;
                bytes[index++] = pixel.A;
            }
        }

        return bytes;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        stream.Write(buffer);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, typeBytes.Length);
        if (data.Length > 0)
        {
            stream.Write(data, 0, data.Length);
        }

        uint crc = ComputeCrc(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static uint ComputeCrc(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte value in typeBytes)
        {
            crc = UpdateCrc(crc, value);
        }

        foreach (byte value in data)
        {
            crc = UpdateCrc(crc, value);
        }

        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320;
        }

        return crc;
    }
}
