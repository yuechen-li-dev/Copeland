namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Untinted OpenPBR dielectric coat on an opaque surface.</summary>
public sealed record Clearcoat3D(float Weight = 1, float Roughness = 0, float IndexOfRefraction = 1.5f)
{
    public bool Active => Weight > 0 && IndexOfRefraction > 1;

    public void Validate()
    {
        if (!float.IsFinite(Weight) || Weight is < 0 or > 1
            || !float.IsFinite(Roughness) || Roughness is < 0 or > 1
            || !float.IsFinite(IndexOfRefraction) || IndexOfRefraction is < 1 or > 3)
        {
            throw new InvalidDataException("AUR-COAT-001: Coat weight and roughness must be in [0, 1] and coat IOR in [1, 3].");
        }
    }
}
