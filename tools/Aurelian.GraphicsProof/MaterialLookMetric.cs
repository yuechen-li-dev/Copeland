using System.Numerics;
using Aurelian.Graphics.Vulkan.Native2D;
using SkiaSharp;

/// <summary>Region-balanced, low-frequency color and luminance-distribution objective.</summary>
internal sealed class MaterialLookMetric
{
    private const int Columns = 24;
    private const int Rows = 14;
    private const int Cells = Columns * Rows;
    private const int Width = ImageGuidedLookResearch.Width;
    private const int Height = ImageGuidedLookResearch.Height;
    private static readonly float[] Decode = Enumerable.Range(0, 256).Select(value => NativeSrgbTransfer.Decode(value / 255f)).ToArray();
    private readonly byte[] regions = new byte[Width * Height];
    private readonly int[][] counts = Enumerable.Range(0, 4).Select(_ => new int[Cells]).ToArray();
    private readonly Features target;
    public byte[] Target { get; }

    public MaterialLookMetric(string output, string split)
    {
        Target = Read(Path.Combine(output, split + "-target.png"));
        byte[] mask = Read(Path.Combine(output, split + "-regions.png"));
        byte[] sourceRegions = mask.Where((_, index) => index % 4 == 0).Select(value => (byte)Math.Round(Decode[value] * 8)).ToArray();
        for (int y = 4; y < Height - 4; y++)
        {
            for (int x = 4; x < Width - 4; x++)
            {
                byte group = sourceRegions[y * Width + x];
                bool inside = group is >= 1 and <= 4;
                for (int dy = -4; dy <= 4 && inside; dy++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        if (sourceRegions[(y + dy) * Width + x + dx] != group)
                        {
                            inside = false;
                            break;
                        }
                    }
                }
                if (inside)
                {
                    regions[y * Width + x] = group;
                    counts[group - 1][Cell(x, y)]++;
                }
            }
        }
        if (counts.Any(group => group.Sum() < 40 || !group.Any(count => count >= 8)))
        {
            throw new InvalidDataException("Every look region needs at least 40 interior pixels and one patch with eight pixels.");
        }
        target = Extract(Target);
    }

    public MaterialLookScore Measure(byte[] pixels)
    {
        Features actual = Extract(pixels);
        var losses = new double[4];
        for (int region = 0; region < 4; region++)
        {
            double sum = 0;
            int cells = 0;
            for (int cell = 0; cell < Cells; cell++)
            {
                if (counts[region][cell] >= 8)
                {
                    sum += Vector3.DistanceSquared(actual.Colors[region][cell], target.Colors[region][cell]);
                    cells++;
                }
            }
            double quantiles = (Math.Abs(actual.Low[region] - target.Low[region]) + Math.Abs(actual.High[region] - target.High[region])) / 2;
            losses[region] = .8 * Math.Sqrt(sum / cells) + .2 * quantiles;
        }
        int admitted = counts.Sum(group => group.Sum());
        return new(losses.Average(), losses, (double)actual.Clipped / (admitted * 3));
    }

    public object Qualify()
    {
        byte[] changed = Target.ToArray();
        for (int pixel = 0; pixel < regions.Length; pixel++)
        {
            if (regions[pixel] == 0)
            {
                changed[pixel * 4] = 255;
                changed[pixel * 4 + 1] = 0;
                changed[pixel * 4 + 2] = 255;
            }
        }
        double identity = Measure(Target).Loss;
        double background = Measure(changed).Loss;
        if (identity != 0 || background != 0)
        {
            throw new InvalidDataException("Material metric identity/background exclusion failed.");
        }
        return new { Identity = identity, BackgroundPerturbation = background, RegionPixels = counts.Select(group => group.Sum()).ToArray() };
    }

    private Features Extract(byte[] pixels)
    {
        var colors = Enumerable.Range(0, 4).Select(_ => new Vector3[Cells]).ToArray();
        var histogram = Enumerable.Range(0, 4).Select(_ => new int[1024]).ToArray();
        int clipped = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int pixel = y * Width + x;
                int region = regions[pixel] - 1;
                if (region < 0)
                {
                    continue;
                }
                byte red = pixels[pixel * 4];
                byte green = pixels[pixel * 4 + 1];
                byte blue = pixels[pixel * 4 + 2];
                Vector3 rgb = new(Decode[red], Decode[green], Decode[blue]);
                colors[region][Cell(x, y)] += rgb;
                double luminance = .2126 * rgb.X + .7152 * rgb.Y + .0722 * rgb.Z;
                histogram[region][Math.Clamp((int)Math.Round(luminance * 1023), 0, 1023)]++;
                clipped += (red >= 254 ? 1 : 0) + (green >= 254 ? 1 : 0) + (blue >= 254 ? 1 : 0);
            }
        }
        var low = new double[4];
        var high = new double[4];
        for (int region = 0; region < 4; region++)
        {
            for (int cell = 0; cell < Cells; cell++)
            {
                if (counts[region][cell] > 0)
                {
                    colors[region][cell] = Lab(colors[region][cell] / counts[region][cell]);
                }
            }
            int total = counts[region].Sum();
            low[region] = Percentile(histogram[region], total, .1);
            high[region] = Percentile(histogram[region], total, .9);
        }
        return new(colors, low, high, clipped);
    }

    private static double Percentile(int[] histogram, int total, double fraction)
    {
        int sum = 0;
        for (int index = 0; index < histogram.Length; index++)
        {
            sum += histogram[index];
            if (sum >= total * fraction)
            {
                return 116 * LabF(index / 1023.0) - 16;
            }
        }
        return 100;
    }

    private static Vector3 Lab(Vector3 rgb)
    {
        double x = LabF((.4124564 * rgb.X + .3575761 * rgb.Y + .1804375 * rgb.Z) / .95047);
        double y = LabF(.2126729 * rgb.X + .7151522 * rgb.Y + .0721750 * rgb.Z);
        double z = LabF((.0193339 * rgb.X + .1191920 * rgb.Y + .9503041 * rgb.Z) / 1.08883);
        return new((float)(116 * y - 16), (float)(500 * (x - y)), (float)(200 * (y - z)));
    }

    private static double LabF(double value) => value > 216.0 / 24389 ? Math.Cbrt(value) : (24389.0 / 27 * value + 16) / 116;
    private static int Cell(int x, int y) => y * Rows / Height * Columns + x * Columns / Width;

    private static byte[] Read(string path)
    {
        using var source = SKBitmap.Decode(path) ?? throw new InvalidDataException(path);
        if (Math.Abs((double)source.Width / source.Height / ((double)Width / Height) - 1) > .02)
        {
            throw new InvalidDataException("Reference aspect ratio changed: " + path);
        }
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(source, new SKRect(0, 0, Width, Height));
        byte[] pixels = new byte[Width * Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return pixels;
    }

    private sealed record Features(Vector3[][] Colors, double[] Low, double[] High, int Clipped);
}

internal sealed record MaterialLookScore(double Loss, double[] Regions, double ClippedChannelFraction);
