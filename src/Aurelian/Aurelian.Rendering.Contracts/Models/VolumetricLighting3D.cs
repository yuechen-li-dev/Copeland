using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Shadowed single scattering. Extinction comes from Fog; colors are scattering albedo.</summary>
public sealed record VolumetricLighting3D
{
    public bool Enabled { get; init; }
    public int PixelSize { get; init; } = 12;
    public int DepthSlices { get; init; } = 32;
    public float MaximumDistance { get; init; } = 60;
    public Vector3 ScatteringAlbedo { get; init; } = new(.85f);
    public Vector3 AmbientRadiance { get; init; } = new(.02f);
    public float Anisotropy { get; init; } = .35f;
    public FogRegion3D? Region { get; init; }

    public void Validate()
    {
        if (PixelSize is < 4 or > 64 || DepthSlices is < 8 or > 64
            || !float.IsFinite(MaximumDistance) || MaximumDistance < 1 || MaximumDistance > 1000
            || !float.IsFinite(Anisotropy) || MathF.Abs(Anisotropy) > .9f
            || !Color(ScatteringAlbedo, 1) || !Color(AmbientRadiance, 1000))
        {
            throw new ArgumentOutOfRangeException(nameof(VolumetricLighting3D), "Volume resolution, distance and scattering must be finite and bounded.");
        }
        Region?.Validate();
    }

    private static bool Color(Vector3 value, float maximum)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z)
            && value.X >= 0 && value.Y >= 0 && value.Z >= 0
            && value.X <= maximum && value.Y <= maximum && value.Z <= maximum;
    }
}

/// <summary>A world-space box restricting the existing exponential height density.</summary>
public sealed record FogRegion3D(Vector3 Minimum, Vector3 Maximum)
{
    public void Validate()
    {
        float[] values = [Minimum.X, Minimum.Y, Minimum.Z, Maximum.X, Maximum.Y, Maximum.Z];
        if (values.Any(value => !float.IsFinite(value) || MathF.Abs(value) > 100000)
            || Minimum.X >= Maximum.X || Minimum.Y >= Maximum.Y || Minimum.Z >= Maximum.Z)
        {
            throw new ArgumentException("A fog region needs finite, ordered world bounds.");
        }
    }
}
