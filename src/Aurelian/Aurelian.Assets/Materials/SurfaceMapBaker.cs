using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Assets.Materials;

/// <summary>One coherent surface sample. Height is in metres; color and all factors are linear.
/// Cavity is an authored ambient-occlusion approximation, not a ray-traced visibility result.</summary>
public readonly record struct SurfaceSample(float HeightMetres, Vector3 BaseColor, float Roughness,
    float Metallic = 0, float Cavity = 1);

public sealed record SurfaceMapSet(ModelTexture BaseColor, ModelTexture Normal, ModelTexture Orm,
    ModelTexture Height, Vector2 TileSizeMetres, float HeightMinimumMetres, float HeightRangeMetres)
{
    public ModelMaterial Material(string slot)
    {
        var sampler = new ModelSampler();
        return new(slot)
        {
            BaseColorTexture = new(BaseColor, sampler),
            NormalTexture = new(Normal, sampler),
            MetallicRoughnessTexture = new(Orm, sampler),
            OcclusionTexture = new(Orm, sampler),
        };
    }
}

/// <summary>Authoring-time CPU bake into the existing glTF material channels. No renderer dependency.
/// The caller supplies a periodic field; wrapped central differences produce tangent-space +Y normals.</summary>
public static class SurfaceMapBaker
{
    public static SurfaceMapSet Bake(string id, int width, int height, Vector2 tileSizeMetres,
        Func<Vector2, SurfaceSample> sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (string.IsNullOrWhiteSpace(id) || width is < 8 or > 1024 || height is < 8 or > 1024
            || !Range(tileSizeMetres.X, .01f, 1000) || !Range(tileSizeMetres.Y, .01f, 1000))
        {
            throw new ArgumentException("AUR-SURFACE-001: Maps require an ID, 8..1024 texels per axis and a tile size of 0.01..1000 metres.");
        }
        var samples = new SurfaceSample[width * height];
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SurfaceSample value = sample(new((x + .5f) / width, (y + .5f) / height));
                if (!Range(value.HeightMetres, -10, 10) || !Range(value.BaseColor.X, 0, 1)
                    || !Range(value.BaseColor.Y, 0, 1) || !Range(value.BaseColor.Z, 0, 1)
                    || !Range(value.Roughness, .045f, 1) || !Range(value.Metallic, 0, 1) || !Range(value.Cavity, 0, 1))
                {
                    throw new ArgumentException($"AUR-SURFACE-002: Invalid finite surface sample at ({x}, {y}).");
                }
                samples[y * width + x] = value;
                minimum = Math.Min(minimum, value.HeightMetres);
                maximum = Math.Max(maximum, value.HeightMetres);
            }
        }
        var albedo = new byte[samples.Length * 4];
        var normals = new byte[albedo.Length];
        var orm = new byte[albedo.Length];
        var heights = new byte[albedo.Length];
        float range = maximum - minimum;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int first = (y * width + x) * 4;
                SurfaceSample value = samples[y * width + x];
                float du = (At(x + 1, y) - At(x - 1, y)) * width / (2 * tileSizeMetres.X);
                float dv = (At(x, y + 1) - At(x, y - 1)) * height / (2 * tileSizeMetres.Y);
                Vector3 normal = Vector3.Normalize(new Vector3(-du, -dv, 1));
                Pixel(albedo, first, new(Srgb(value.BaseColor.X), Srgb(value.BaseColor.Y), Srgb(value.BaseColor.Z)));
                Pixel(normals, first, normal * .5f + new Vector3(.5f));
                Pixel(orm, first, new(value.Cavity, value.Roughness, value.Metallic));
                float relative = range > 0 ? (value.HeightMetres - minimum) / range : 0;
                Pixel(heights, first, new(relative));
            }
        }
        return new(Texture("base-color", albedo), Texture("normal", normals), Texture("orm", orm),
            Texture("height", heights), tileSizeMetres, minimum, range);

        float At(int x, int y)
        {
            int wrappedX = (x + width) % width;
            int wrappedY = (y + height) % height;
            return samples[wrappedY * width + wrappedX].HeightMetres;
        }

        ModelTexture Texture(string channel, byte[] bytes)
        {
            string digest = Convert.ToHexString(SHA256.HashData(bytes));
            return new($"{id}/{channel}/{width}x{height}/{digest}", width, height, bytes.ToImmutableArray());
        }
    }

    private static void Pixel(byte[] bytes, int first, Vector3 value)
    {
        bytes[first] = Encode(value.X);
        bytes[first + 1] = Encode(value.Y);
        bytes[first + 2] = Encode(value.Z);
        bytes[first + 3] = 255;
    }

    private static byte Encode(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);

    private static float Srgb(float linear)
    {
        if (linear <= .0031308f) { return linear * 12.92f; }
        return 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f;
    }

    private static bool Range(float value, float minimum, float maximum)
    {
        return float.IsFinite(value) && value >= minimum && value <= maximum;
    }
}
