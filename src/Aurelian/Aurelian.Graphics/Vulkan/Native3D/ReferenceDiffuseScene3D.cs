using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Capture adapter for the same explicit Lambertian triangles used by static
/// diffuse compilation. Preserves face albedo, winding, and additive two-sided emission.</summary>
public static class ReferenceDiffuseScene3D
{
    public static ReferenceTriangle3D[] Describe(StaticDiffuseLightingRecipe recipe)
    {
        recipe.Validate();
        var triangles = new List<ReferenceTriangle3D>();
        foreach (StaticDiffuseContributor contributor in recipe.Contributors)
        {
            for (int offset = 0; offset < contributor.Positions.Length; offset += 9)
            {
                Vector3 a = Read3(contributor.Positions, offset);
                Vector3 b = Read3(contributor.Positions, offset + 3);
                Vector3 c = Read3(contributor.Positions, offset + 6);
                Vector3 albedo = contributor.FaceAlbedos.Length == 0
                    ? Read3(contributor.Albedo, 0)
                    : Read3(contributor.FaceAlbedos, offset / 3);
                var material = new ReferenceMaterial3D(albedo, Dielectric: 0,
                    Emission: Read3(contributor.Emission, 0), TwoSidedEmission: true);
                triangles.Add(new(a, b, c, material));
            }
        }
        return triangles.ToArray();
    }

    private static Vector3 Read3(float[] values, int offset)
    {
        return new(values[offset], values[offset + 1], values[offset + 2]);
    }
}
