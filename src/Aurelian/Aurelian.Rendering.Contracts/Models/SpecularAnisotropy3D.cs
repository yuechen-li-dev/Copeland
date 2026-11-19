namespace Aurelian.Rendering.Contracts.Models;

public enum AnisotropyConvention3D
{
    OpenPbr,
    Gltf,
}

/// <summary>Specular stretch about the mesh normal, counterclockwise from tangent toward bitangent.</summary>
public sealed record SpecularAnisotropy3D(float Strength, float RotationRadians = 0,
    AnisotropyConvention3D Convention = AnisotropyConvention3D.OpenPbr)
{
    public bool Active => Strength > 0;

    public void Validate()
    {
        if (!float.IsFinite(Strength) || Strength is < 0 or > 1 || !float.IsFinite(RotationRadians)
            || !Enum.IsDefined(Convention))
        {
            throw new InvalidDataException("AUR-ANISO-001: Anisotropy requires a finite strength in [0, 1], a finite rotation in radians and a supported convention.");
        }
    }
}
