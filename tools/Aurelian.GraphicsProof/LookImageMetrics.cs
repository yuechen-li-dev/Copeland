using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Native2D;
using SkiaSharp;

/// <summary>Deliberately low-frequency foreground color proxy; it does not measure realism.</summary>
internal sealed class LookImageMetrics
{
    private const int Columns = 24;
    private const int Rows = 14;
    private readonly bool[] foreground;
    private readonly int[] counts = new int[Columns * Rows];
    private readonly Vector3[] targetFeatures;
    internal byte[] Target { get; }
    private static int Width => ImageGuidedLookResearch.Width;
    private static int Height => ImageGuidedLookResearch.Height;

    private LookImageMetrics(byte[] target, byte[] mask)
    {
        Target = target;
        foreground = new bool[Width * Height];
        for (int y = 4; y < Height - 4; y++)
        {
            for (int x = 4; x < Width - 4; x++)
            {
                bool inside = true;
                for (int dy = -4; dy <= 4 && inside; dy++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        if (mask[((y + dy) * Width + x + dx) * 4] < 240)
                        {
                            inside = false;
                            break;
                        }
                    }
                }
                foreground[y * Width + x] = inside;
                if (inside)
                {
                    counts[Cell(x, y)]++;
                }
            }
        }
        if (counts.Sum() < 1000)
        {
            throw new InvalidDataException("The game foreground mask is empty or too small.");
        }
        targetFeatures = Features(target);
    }

    internal static LookImageMetrics Load(string output, string split)
    {
        return new(Read(Path.Combine(output, split + "-target.png")), Read(Path.Combine(output, split + "-mask.png")));
    }

    internal double Loss(byte[] pixels)
    {
        Vector3[] features = Features(pixels);
        double squared = 0;
        int cells = 0;
        for (int index = 0; index < features.Length; index++)
        {
            if (counts[index] >= 16)
            {
                squared += Vector3.DistanceSquared(features[index], targetFeatures[index]);
                cells++;
            }
        }
        return Math.Sqrt(squared / cells);
    }

    internal object Qualify()
    {
        byte[] backgroundControl = Target.ToArray();
        for (int pixel = 0; pixel < foreground.Length; pixel++)
        {
            if (!foreground[pixel])
            {
                backgroundControl[pixel * 4] = 255;
                backgroundControl[pixel * 4 + 1] = 0;
                backgroundControl[pixel * 4 + 2] = 255;
            }
        }
        double selfLoss = Loss(Target);
        double backgroundLoss = Loss(backgroundControl);
        if (selfLoss != 0 || backgroundLoss != 0)
        {
            throw new InvalidDataException("The appearance metric failed its identity/background-exclusion controls.");
        }
        return new
        {
            ForegroundPixels = counts.Sum(),
            AdmittedCells = counts.Count(count => count >= 16),
            SelfLoss = selfLoss,
            BackgroundPerturbationLoss = backgroundLoss
        };
    }

    internal double Clipping(byte[] pixels)
    {
        int clipped = 0;
        for (int pixel = 0; pixel < foreground.Length; pixel++)
        {
            if (!foreground[pixel])
            {
                continue;
            }
            for (int channel = 0; channel < 3; channel++)
            {
                if (pixels[pixel * 4 + channel] >= 254)
                {
                    clipped++;
                }
            }
        }
        return (double)clipped / (counts.Sum() * 3);
    }

    private Vector3[] Features(byte[] pixels)
    {
        var sums = new Vector3[Columns * Rows];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int pixel = y * Width + x;
                if (foreground[pixel])
                {
                    sums[Cell(x, y)] += new Vector3(NativeSrgbTransfer.Decode(pixels[pixel * 4] / 255f),
                        NativeSrgbTransfer.Decode(pixels[pixel * 4 + 1] / 255f),
                        NativeSrgbTransfer.Decode(pixels[pixel * 4 + 2] / 255f));
                }
            }
        }
        for (int index = 0; index < sums.Length; index++)
        {
            if (counts[index] > 0)
            {
                sums[index] = Lab(sums[index] / counts[index]);
            }
        }
        return sums;
    }

    private static Vector3 Lab(Vector3 rgb)
    {
        double x = (.4124564 * rgb.X + .3575761 * rgb.Y + .1804375 * rgb.Z) / .95047;
        double y = .2126729 * rgb.X + .7151522 * rgb.Y + .0721750 * rgb.Z;
        double z = (.0193339 * rgb.X + .1191920 * rgb.Y + .9503041 * rgb.Z) / 1.08883;
        static double F(double value)
        {
            return value > 216.0 / 24389 ? Math.Cbrt(value) : (24389.0 / 27 * value + 16) / 116;
        }
        double fx = F(x);
        double fy = F(y);
        double fz = F(z);
        return new((float)(116 * fy - 16), (float)(500 * (fx - fy)), (float)(200 * (fy - fz)));
    }

    private static int Cell(int x, int y) => y * Rows / Height * Columns + x * Columns / Width;

    private static byte[] Read(string path)
    {
        using var source = SKBitmap.Decode(path) ?? throw new InvalidDataException("Cannot decode " + path);
        double aspectError = Math.Abs((double)source.Width / source.Height / ((double)Width / Height) - 1);
        if (aspectError > .02)
        {
            throw new InvalidDataException($"Reference aspect ratio drifted by {aspectError:P2}: {path}.");
        }
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(source, new SKRect(0, 0, Width, Height));
        var pixels = new byte[Width * Height * 4];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return pixels;
    }

    internal static void Sheet(string path, LookImageMetrics training, LookImageMetrics heldout,
        byte[] trainBaseline, byte[] trainSelected, byte[] heldoutBaseline, byte[] heldoutSelected)
    {
        const int TileWidth = 640;
        const int TileHeight = 360;
        using var sheet = new SKBitmap(TileWidth * 3, (TileHeight + 42) * 2);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(16, 20, 27));
        using var font = new SKFont(SKTypeface.Default, 21);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        byte[][] pixels = [trainBaseline, training.Target, trainSelected, heldoutBaseline, heldout.Target, heldoutSelected];
        string[] labels = ["TRAIN / BASELINE", "TRAIN / AI TARGET", "TRAIN / SELECTED", "HELD OUT / BASELINE", "HELD OUT / AI TARGET", "HELD OUT / SELECTED"];
        for (int index = 0; index < pixels.Length; index++)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(pixels[index], 0, bitmap.GetPixels(), pixels[index].Length);
            int x = index % 3 * TileWidth;
            int y = index / 3 * (TileHeight + 42);
            canvas.DrawText(labels[index], x + 15, y + 29, font, paint);
            canvas.DrawBitmap(bitmap, new SKRect(x, y + 42, x + TileWidth, y + 42 + TileHeight));
        }
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}
