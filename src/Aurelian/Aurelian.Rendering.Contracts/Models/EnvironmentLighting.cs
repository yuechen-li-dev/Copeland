using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Six octahedral GGX bands, cosine diffuse and integrated BRDF. No runtime integration.</summary>
public sealed class EnvironmentLighting
{
    public const int Size = 64;
    public const int Bands = 8;
    public const string Decoder = "aurelian.environment/octa-ggx6-cosine-dfg-v1";
    public ImmutableArray<float> Pixels { get; }
    public string ContentKey { get; }
    public string SourceKey { get; }

    public EnvironmentLighting(IEnumerable<float> pixels, string sourceKey)
    {
        Pixels = pixels.ToImmutableArray();
        if (Pixels.Length != Size * Size * Bands * 4 || string.IsNullOrWhiteSpace(sourceKey)
            || Pixels.Any(value => !float.IsFinite(value) || value < 0 || value > 60000))
        {
            throw new InvalidDataException("AUR-ENV-001: Invalid environment atlas dimensions or finite HDR radiance.");
        }
        SourceKey = sourceKey;
        ContentKey = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(Pixels.AsSpan())));
    }
}
