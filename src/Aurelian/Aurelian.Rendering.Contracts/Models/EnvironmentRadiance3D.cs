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
        ContentKey = width + "x" + height + ":" + Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(Rgba.AsSpan())));
    }
}
