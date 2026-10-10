using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Analytic single-scattering height fog. Density is extinction per metre.</summary>
public sealed record HeightFog3D
{
    public float Density { get; init; }
    public float HeightFalloff { get; init; } = .15f;
    public float BaseHeight { get; init; }
    public float StartDistance { get; init; }
    public float MaximumDistance { get; init; } = 1000;
    public Vector3 Color { get; init; } = new(.45f, .55f, .7f);

    public void Validate()
    {
        if (!Range(Density, 0, 10) || !Range(HeightFalloff, 0, 10)
            || !Range(BaseHeight, -100000, 100000) || !Range(StartDistance, 0, 100000)
            || !Range(MaximumDistance, .01f, 100000) || StartDistance >= MaximumDistance
            || !Range(Color.X, 0, 1000) || !Range(Color.Y, 0, 1000) || !Range(Color.Z, 0, 1000))
        {
            throw new ArgumentOutOfRangeException(nameof(HeightFog3D), "Fog must have finite linear radiance and a bounded distance interval.");
        }
    }

    private static bool Range(float value, float minimum, float maximum)
    {
        return float.IsFinite(value) && value >= minimum && value <= maximum;
    }
}
