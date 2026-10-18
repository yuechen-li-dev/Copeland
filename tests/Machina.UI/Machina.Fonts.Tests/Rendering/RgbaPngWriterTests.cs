using System.Buffers.Binary;
using System.IO.Compression;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

public sealed class RgbaPngWriterTests
{
    [Fact]
    public void PngWriter_PreservesRgbaPixelsAndRowOrder()
    {
        Rgba32[] pixels =
        [
            new Rgba32(255, 0, 0, 255),
            new Rgba32(0, 255, 0, 128),
            new Rgba32(0, 0, 255, 64),
            new Rgba32(255, 255, 255, 0),
        ];
        RgbaImage image = new(2, 2, pixels);
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "proof.png");

        try
        {
            RgbaPngWriter.Write(path, image);

            DecodedPng decoded = DecodePng(File.ReadAllBytes(path));

            Assert.Equal(2, decoded.Width);
            Assert.Equal(2, decoded.Height);
            Assert.Equal(pixels, decoded.Pixels);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PngWriter_RejectsInvalidInput()
    {
        RgbaImage image = new(1, 1);

        Assert.Throws<ArgumentException>(() => RgbaPngWriter.Write(string.Empty, image));
        Assert.Throws<ArgumentNullException>(() => RgbaPngWriter.Write("image.png", null!));
    }

    private static DecodedPng DecodePng(byte[] bytes)
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        Assert.True(bytes.AsSpan(0, signature.Length).SequenceEqual(signature));

        int width = 0;
        int height = 0;
        List<byte> compressedImageData = [];
        int offset = signature.Length;
        bool foundEnd = false;

        while (offset < bytes.Length)
        {
            int chunkLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, sizeof(int)));
            offset += sizeof(int);
            string chunkType = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            offset += 4;

            if (chunkLength < 0 || offset + chunkLength + sizeof(uint) > bytes.Length)
            {
                throw new InvalidDataException("PNG chunk length is invalid.");
            }

            ReadOnlySpan<byte> chunkData = bytes.AsSpan(offset, chunkLength);
            offset += chunkLength + sizeof(uint);

            if (chunkType == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(chunkData[..sizeof(int)]);
                height = BinaryPrimitives.ReadInt32BigEndian(chunkData.Slice(sizeof(int), sizeof(int)));
                Assert.Equal((byte)8, chunkData[8]);
                Assert.Equal((byte)6, chunkData[9]);
            }
            else if (chunkType == "IDAT")
            {
                compressedImageData.AddRange(chunkData.ToArray());
            }
            else if (chunkType == "IEND")
            {
                foundEnd = true;
                break;
            }
        }

        Assert.True(foundEnd);
        Assert.Equal(bytes.Length, offset);

        using MemoryStream compressedStream = new(compressedImageData.ToArray());
        using ZLibStream zlib = new(compressedStream, CompressionMode.Decompress);
        using MemoryStream pixelStream = new();
        zlib.CopyTo(pixelStream);
        byte[] scanlines = pixelStream.ToArray();
        Rgba32[] pixels = new Rgba32[checked(width * height)];
        int scanlineOffset = 0;
        int pixelIndex = 0;

        for (int y = 0; y < height; y++)
        {
            Assert.Equal((byte)0, scanlines[scanlineOffset++]);

            for (int x = 0; x < width; x++)
            {
                pixels[pixelIndex++] = new Rgba32(
                    scanlines[scanlineOffset++],
                    scanlines[scanlineOffset++],
                    scanlines[scanlineOffset++],
                    scanlines[scanlineOffset++]);
            }
        }

        Assert.Equal(scanlines.Length, scanlineOffset);
        return new DecodedPng(width, height, pixels);
    }

    private sealed record DecodedPng(int Width, int Height, Rgba32[] Pixels);
}
