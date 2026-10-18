using SkiaSharp;

namespace TinyFarm.Native;

/// <summary>
/// A decoded image as straight (unpremultiplied) BGRA rows, the layout the art loaders were written against.
/// Decoding uses SkiaSharp with no colour-space conversion, so it works on every platform the shell runs on.
/// </summary>
internal sealed class TinyFarmImage
{
    private readonly byte[] bgra;

    private TinyFarmImage(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        this.bgra = bgra;
    }

    public int Width { get; }
    public int Height { get; }

    public static TinyFarmImage Load(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"Cannot decode image '{path}'.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new InvalidDataException($"Cannot decode image '{path}': {result}.");
        }
        return new TinyFarmImage(info.Width, info.Height, bitmap.Bytes);
    }

    /// <summary>Wrap pixels drawn with SkiaSharp (for example procedural art).</summary>
    public static TinyFarmImage From(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888)
        {
            throw new ArgumentException("Procedural art must be drawn into a BGRA bitmap.", nameof(bitmap));
        }
        using var straight = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        if (!bitmap.CopyTo(straight, SKColorType.Bgra8888))
        {
            // Older SkiaSharp builds cannot unpremultiply in CopyTo; draw through a canvas instead.
            using var canvas = new SKCanvas(straight);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        return new TinyFarmImage(bitmap.Width, bitmap.Height, straight.Bytes);
    }

    /// <summary>Copy <paramref name="row"/>.Length bytes of BGRA pixels starting at column <paramref name="x"/> of row <paramref name="y"/>.</summary>
    public void CopyRowBgra(int y, int x, byte[] row)
    {
        if (y < 0 || y >= Height || x < 0 || x * 4 + row.Length > Width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Row copy is outside the image.");
        }
        Buffer.BlockCopy(bgra, (y * Width + x) * 4, row, 0, row.Length);
    }

    /// <summary>The whole image as straight RGBA.</summary>
    public byte[] ToRgba()
    {
        var rgba = new byte[bgra.Length];
        for (int index = 0; index < bgra.Length; index += 4)
        {
            rgba[index] = bgra[index + 2];
            rgba[index + 1] = bgra[index + 1];
            rgba[index + 2] = bgra[index];
            rgba[index + 3] = bgra[index + 3];
        }
        return rgba;
    }
}
