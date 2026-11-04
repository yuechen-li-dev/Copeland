using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Immutable scene-linear equirectangular source shared by environment
/// compilation and reference transport. Longitude is atan2(z,x); +Y is the north pole.</summary>
public sealed class EnvironmentRadiance3D
{
    public int Width { get; }
    public int Height { get; }
    public ImmutableArray<float> Rgba { get; }
    public string ContentKey { get; }
    public bool IsConstant { get; }
    public int ImportanceWidth => Math.Min(Width, 64);
    public int ImportanceHeight => Math.Min(Height, 32);
    public ImmutableArray<float> ImportanceCdf { get; }

    public EnvironmentRadiance3D(int width, int height, IEnumerable<float> rgba)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 2048)
            throw new ArgumentException("AUR-ENV-003: Invalid source environment dimensions.");
        Rgba = rgba.ToImmutableArray();
        if (Rgba.Length != width * height * 4 || Rgba.Any(value => !float.IsFinite(value) || value < 0 || value > 60000))
            throw new ArgumentException("AUR-ENV-003: Source environment requires finite nonnegative RGBA radiance within 60000.");
        Width = width;
        Height = height;
        IsConstant = true;
        for (int offset = 4; offset < Rgba.Length; offset += 4)
        {
            if (Rgba[offset] != Rgba[0] || Rgba[offset + 1] != Rgba[1] || Rgba[offset + 2] != Rgba[2])
            {
                IsConstant = false;
                break;
            }
        }
        ImportanceCdf = BuildImportanceCdf();
        ContentKey = width + "x" + height + ":" + Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(Rgba.AsSpan())));
    }

    private ImmutableArray<float> BuildImportanceCdf()
    {
        double[] weights = new double[ImportanceWidth * ImportanceHeight];
        for (int y = 0; y < Height; y++)
        {
            double solidAngle = Math.Tau / Width * (Math.Cos(Math.PI * y / Height)
                - Math.Cos(Math.PI * (y + 1) / Height));
            for (int x = 0; x < Width; x++)
            {
                int pixel = (y * Width + x) * 4;
                double luminance = Rgba[pixel] * .2126 + Rgba[pixel + 1] * .7152 + Rgba[pixel + 2] * .0722;
                int bin = (y * ImportanceHeight / Height) * ImportanceWidth + x * ImportanceWidth / Width;
                weights[bin] += luminance * solidAngle;
            }
        }
        double sum = weights.Sum();
        float[] cdf = new float[weights.Length];
        double cumulative = 0;
        for (int index = 0; index < cdf.Length; index++)
        {
            cumulative += sum > 0 ? weights[index] / sum : 1.0 / weights.Length;
            cdf[index] = (float)Math.Min(cumulative, 1);
        }
        cdf[^1] = 1;
        return cdf.ToImmutableArray();
    }
}
