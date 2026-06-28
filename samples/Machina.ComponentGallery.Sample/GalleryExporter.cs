using System.IO.Compression;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Machina.Pipeline;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Standard.Theme;

namespace Machina.ComponentGallery.Sample;

public static class GalleryExporter
{
    public static GalleryExportResult Export(GalleryProgramOptions options)
    {
        return Export(options.InitialState, options.ExportDirectory, options.ExportName, options.IncludeMsdfFontProof);
    }

    public static GalleryExportResult Export(
        GalleryState state,
        string outputDirectory,
        string exportName,
        bool includeMsdfFontProof = false)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        var outputPath = BuildOutputPath(fullOutputDirectory, exportName);
        var frame = new MachinaRasterPipeline().Render(
            GalleryScreen.Build(state, includeMsdfFontProof, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);
        GalleryMsdfFontProofPlacement? msdfProofPlacement = null;

        if (includeMsdfFontProof)
        {
            msdfProofPlacement = GalleryMsdfFontProofRenderer.BlitProof(frame.RasterFrame, frame.Resolved);
        }

        PngRasterWriter.Write(outputPath, frame.RasterFrame);

        return new GalleryExportResult(
            OutputPath: outputPath,
            Width: frame.RasterFrame.Width,
            Height: frame.RasterFrame.Height,
            IncludeMsdfFontProof: includeMsdfFontProof,
            MsdfProofPlacement: msdfProofPlacement);
    }

    public static WriteableBitmap ToBitmap(RasterFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var locked = bitmap.Lock();
        var pixelBytes = ToRgbaBytes(frame);
        System.Runtime.InteropServices.Marshal.Copy(pixelBytes, 0, locked.Address, pixelBytes.Length);

        return bitmap;
    }

    private static string BuildOutputPath(string outputDirectory, string exportName)
    {
        var fileName = exportName;
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension))
        {
            fileName += ".png";
        }
        else if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Gallery export names must omit the extension or end with .png.");
        }

        return Path.Combine(outputDirectory, fileName);
    }

    private static byte[] ToRgbaBytes(RasterFrame frame)
    {
        var width = frame.Surface.Width;
        var height = frame.Surface.Height;
        var bytes = new byte[width * height * 4];
        var index = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
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

    private static class PngRasterWriter
    {
        private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly byte[] IhdrType = "IHDR"u8.ToArray();
        private static readonly byte[] IdatType = "IDAT"u8.ToArray();
        private static readonly byte[] IendType = "IEND"u8.ToArray();

        public static void Write(string outputPath, RasterFrame frame)
        {
            using var stream = File.Create(outputPath);

            stream.Write(PngSignature, 0, PngSignature.Length);
            WriteChunk(stream, IhdrType, BuildHeader(frame.Width, frame.Height));
            WriteChunk(stream, IdatType, CompressImageData(frame));
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

        private static byte[] CompressImageData(RasterFrame frame)
        {
            var rgbaBytes = ToRgbaBytes(frame);
            var rowLength = (frame.Width * 4) + 1;
            var rawImageData = new byte[rowLength * frame.Height];
            var sourceIndex = 0;
            var targetIndex = 0;

            for (var y = 0; y < frame.Height; y++)
            {
                rawImageData[targetIndex++] = 0;

                Buffer.BlockCopy(rgbaBytes, sourceIndex, rawImageData, targetIndex, frame.Width * 4);
                sourceIndex += frame.Width * 4;
                targetIndex += frame.Width * 4;
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, leaveOpen: true))
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
}

public sealed record GalleryExportResult(
    string OutputPath,
    int Width,
    int Height,
    bool IncludeMsdfFontProof,
    GalleryMsdfFontProofPlacement? MsdfProofPlacement);
