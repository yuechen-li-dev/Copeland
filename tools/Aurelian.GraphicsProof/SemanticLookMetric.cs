using System.Numerics;
using System.Text.Json;
using Aurelian.Graphics.Vulkan.Native2D;
using SkiaSharp;

/// <summary>Independent material swatches: no pixel correspondence between reference and renderer.</summary>
internal sealed class SemanticLookMetric
{
    private const int Width = ImageGuidedLookResearch.Width;
    private const int Height = ImageGuidedLookResearch.Height;
    private const int Bins = 512;
    private static readonly float[] Linear = Enumerable.Range(0, 256)
        .Select(value => NativeSrgbTransfer.Decode(value / 255f)).ToArray();
    private static readonly double[] Quantiles = [.1, .25, .5, .75, .9];
    private readonly byte[] sourceRegions;
    private readonly byte[] targetRegions;
    private readonly Features target;
    public byte[] Target { get; }

    public SemanticLookMetric(string output, string split, byte[]? sourceMaskOverride = null)
    {
        Target = Read(Path.Combine(output, split + "-target.png"));
        byte[] sourceMask = sourceMaskOverride ?? Read(Path.Combine(output, split + "-regions.png"));
        sourceRegions = Erode(sourceMask.Where((_, index) => index % 4 == 0)
            .Select(value => (byte)Math.Round(Linear[value] * 8)).ToArray());
        var patches = JsonSerializer.Deserialize<RegionPatch[]>(
            File.ReadAllText(Path.Combine(output, split + "-patches.json")))!;
        using var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        foreach (RegionPatch patch in patches)
        {
            if (patch.Region is < 1 or > 4 || patch.Points.Length < 3
                || patch.Points.Any(point => point.Length != 2 || point[0] < 0 || point[0] >= Width || point[1] < 0 || point[1] >= Height))
            {
                throw new InvalidDataException("Reference patches require a material ID 1..4 and in-frame polygon points.");
            }
            using var path = new SKPath();
            path.MoveTo(patch.Points[0][0], patch.Points[0][1]);
            foreach (int[] point in patch.Points.Skip(1))
            {
                path.LineTo(point[0], point[1]);
            }
            path.Close();
            using var paint = new SKPaint { Color = new SKColor((byte)patch.Region, 0, 0), IsAntialias = false };
            canvas.DrawPath(path, paint);
        }
        byte[] labels = new byte[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                labels[y * Width + x] = bitmap.GetPixel(x, y).Red;
            }
        }
        targetRegions = Erode(labels);
        target = Extract(Target, targetRegions);
        WriteLabels(output, split + "-target-labels.png", targetRegions);
    }

    public SemanticLookScore Measure(byte[] pixels)
    {
        Features actual = Extract(pixels, sourceRegions);
        return new(Distance(actual, target), actual.Regions.Select((region, index) => RegionDistance(region, target.Regions[index])).ToArray(),
            actual.ClippedFraction);
    }

    public object Qualify()
    {
        byte[] perturbed = Target.ToArray();
        byte[] shifted = new byte[Target.Length];
        byte[] shiftedLabels = new byte[targetRegions.Length];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int pixel = y * Width + x;
                if (targetRegions[pixel] == 0)
                {
                    perturbed[pixel * 4] = 255;
                    perturbed[pixel * 4 + 1] = 0;
                    perturbed[pixel * 4 + 2] = 255;
                    continue;
                }
                if (x + 8 >= Width)
                {
                    throw new InvalidDataException("Translation control would clip a labelled reference patch.");
                }
                int destination = pixel + 8;
                shiftedLabels[destination] = targetRegions[pixel];
                Array.Copy(Target, pixel * 4, shifted, destination * 4, 4);
            }
        }
        double identity = Distance(target, target);
        double background = Distance(Extract(perturbed, targetRegions), target);
        double translation = Distance(Extract(shifted, shiftedLabels), target);
        if (identity != 0 || background != 0 || translation != 0)
        {
            throw new InvalidDataException("Semantic metric identity, background or joint image/label translation control failed.");
        }
        return new
        {
            Identity = identity,
            BackgroundPerturbation = background,
            ImageAndLabelsTranslatedEightPixels = translation,
            SourcePixels = Enumerable.Range(1, 4).Select(group => sourceRegions.Count(value => value == group)).ToArray(),
            TargetSamples = target.Regions.Select(region => region.Count).ToArray(),
        };
    }

    private static byte[] Erode(byte[] labels)
    {
        byte[] result = new byte[labels.Length];
        for (int y = 4; y < Height - 4; y++)
        {
            for (int x = 4; x < Width - 4; x++)
            {
                byte region = labels[y * Width + x];
                bool interior = region is >= 1 and <= 4;
                for (int dy = -4; dy <= 4 && interior; dy++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        if (labels[(y + dy) * Width + x + dx] != region)
                        {
                            interior = false;
                            break;
                        }
                    }
                }
                if (interior)
                {
                    result[y * Width + x] = region;
                }
            }
        }
        return result;
    }

    private static Features Extract(byte[] pixels, byte[] labels)
    {
        var regions = Enumerable.Range(0, 4).Select(_ => new RegionFeatures()).ToArray();
        int clipped = 0;
        int admitted = 0;
        for (int pixel = 0; pixel < labels.Length; pixel++)
        {
            int region = labels[pixel] - 1;
            if (region is < 0 or > 3)
            {
                continue;
            }
            byte red = pixels[pixel * 4];
            byte green = pixels[pixel * 4 + 1];
            byte blue = pixels[pixel * 4 + 2];
            admitted++;
            clipped += (red >= 254 ? 1 : 0) + (green >= 254 ? 1 : 0) + (blue >= 254 ? 1 : 0);
            if (pixel % Width % 2 != 0 || pixel / Width % 2 != 0)
            {
                continue;
            }
            Vector3 lab = Lab(new(Linear[red], Linear[green], Linear[blue]));
            RegionFeatures features = regions[region];
            features.Sum += lab;
            features.Count++;
            features.Histogram[0][Bin(lab.X, 0, 100)]++;
            features.Histogram[1][Bin(lab.Y, -128, 128)]++;
            features.Histogram[2][Bin(lab.Z, -128, 128)]++;
        }
        if (regions.Any(region => region.Count < 60))
        {
            throw new InvalidDataException("Each independent material swatch needs at least 60 interior samples.");
        }
        return new(regions, (double)clipped / (admitted * 3));
    }

    private static double Distance(Features first, Features second)
    {
        return first.Regions.Select((region, index) => RegionDistance(region, second.Regions[index])).Average();
    }

    private static double RegionDistance(RegionFeatures first, RegionFeatures second)
    {
        double mean = Vector3.Distance(first.Sum / first.Count, second.Sum / second.Count);
        double sum = 0;
        for (int channel = 0; channel < 3; channel++)
        {
            foreach (double fraction in Quantiles)
            {
                double difference = Percentile(first, channel, fraction) - Percentile(second, channel, fraction);
                sum += difference * difference;
            }
        }
        return .5 * mean + .5 * Math.Sqrt(sum / Quantiles.Length);
    }

    private static int Bin(float value, float minimum, float maximum)
    {
        return Math.Clamp((int)Math.Round((value - minimum) / (maximum - minimum) * (Bins - 1)), 0, Bins - 1);
    }

    private static double Percentile(RegionFeatures features, int channel, double fraction)
    {
        int accumulated = 0;
        for (int index = 0; index < Bins; index++)
        {
            accumulated += features.Histogram[channel][index];
            if (accumulated >= features.Count * fraction)
            {
                return channel == 0 ? index * 100.0 / (Bins - 1) : index * 256.0 / (Bins - 1) - 128;
            }
        }
        throw new InvalidDataException("Incomplete semantic histogram.");
    }

    private static Vector3 Lab(Vector3 rgb)
    {
        static double F(double value) => value > 216.0 / 24389 ? Math.Cbrt(value) : (24389.0 / 27 * value + 16) / 116;
        double x = F((.4124564 * rgb.X + .3575761 * rgb.Y + .1804375 * rgb.Z) / .95047);
        double y = F(.2126729 * rgb.X + .7151522 * rgb.Y + .0721750 * rgb.Z);
        double z = F((.0193339 * rgb.X + .1191920 * rgb.Y + .9503041 * rgb.Z) / 1.08883);
        return new((float)(116 * y - 16), (float)(500 * (x - y)), (float)(200 * (y - z)));
    }

    private static byte[] Read(string path)
    {
        using var source = SKBitmap.Decode(path) ?? throw new InvalidDataException(path);
        if (Math.Abs((double)source.Width / source.Height / ((double)Width / Height) - 1) > .02)
        {
            throw new InvalidDataException("Reference aspect ratio is incompatible: " + path);
        }
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(source, new SKRect(0, 0, Width, Height));
        byte[] pixels = new byte[Width * Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return pixels;
    }

    private static void WriteLabels(string output, string name, byte[] labels)
    {
        Vector3[] colors = [Vector3.Zero, new(255, 100, 0), new(80, 190, 40), new(140, 80, 40), new(190, 190, 190)];
        byte[] pixels = new byte[labels.Length * 4];
        for (int index = 0; index < labels.Length; index++)
        {
            Vector3 color = colors[labels[index]];
            pixels[index * 4] = (byte)color.X;
            pixels[index * 4 + 1] = (byte)color.Y;
            pixels[index * 4 + 2] = (byte)color.Z;
            pixels[index * 4 + 3] = 255;
        }
        ImageGuidedLookResearch.Save(output, name, pixels);
    }

    private sealed record RegionPatch(int Region, int[][] Points);
    private sealed record Features(RegionFeatures[] Regions, double ClippedFraction);
    private sealed class RegionFeatures
    {
        public Vector3 Sum;
        public int Count;
        public int[][] Histogram { get; } = [new int[Bins], new int[Bins], new int[Bins]];
    }
}

internal sealed record SemanticLookScore(double Loss, double[] Regions, double ClippedChannelFraction);
