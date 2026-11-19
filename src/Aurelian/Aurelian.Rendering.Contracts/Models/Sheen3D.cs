using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>OpenPBR fuzz coverage, linear reflection tint and fiber roughness.</summary>
public sealed record Sheen3D(Vector3 Color, float Roughness = .5f, float Weight = 1)
{
    public bool Active => Weight > 0;

    public void Validate()
    {
        if (!Unit(Color.X) || !Unit(Color.Y) || !Unit(Color.Z) || !Unit(Roughness) || !Unit(Weight))
        {
            throw new InvalidDataException("AUR-SHEEN-001: Sheen color, roughness and weight must be finite and within [0, 1].");
        }
    }

    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}
