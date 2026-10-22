using System.Numerics;

namespace Aurelian.Assets.Materials;

/// <summary>Small periodic material recipes. Bake once, share their immutable textures between instances.</summary>
public static class ProceduralSurfaceMaps
{
    public static SurfaceMapSet Masonry(string id, uint seed = 1, int resolution = 256)
    {
        return Blocks(id, seed, resolution, columns: 4, rows: 8, stagger: true,
            new(.06f, .048f, .038f), relief: .005f);
    }

    public static SurfaceMapSet Pavers(string id, uint seed = 1, int resolution = 256)
    {
        return Blocks(id, seed, resolution, columns: 2, rows: 2, stagger: false,
            new(.085f, .09f, .095f), relief: .003f);
    }

    public static SurfaceMapSet Asphalt(string id, float wetness = .85f, uint seed = 1, int resolution = 256)
    {
        if (!float.IsFinite(wetness) || wetness < 0 || wetness > 1)
        {
            throw new ArgumentException("AUR-SURFACE-003: Wetness must be in 0..1.");
        }
        return SurfaceMapBaker.Bake(id, resolution, resolution, Vector2.One, uv =>
        {
            float aggregate = Noise(uv, 64, seed);
            float grain = Noise(uv, 96, seed + 19);
            float broad = Noise(uv, 8, seed + 31);
            float pits = Smooth(.62f, .86f, Noise(uv, 32, seed + 7));
            float height = (aggregate - .5f) * .0012f + (grain - .5f) * .00035f - pits * .0008f;
            float damp = 1 - wetness * .68f;
            Vector3 color = new Vector3(.075f, .082f, .092f) * damp * (.75f + aggregate * .5f);
            float dry = .80f + broad * .14f;
            float wet = .18f + broad * .20f + grain * .05f;
            return new(height, color, dry * (1 - wetness) + wet * wetness, Cavity: 1 - pits * .12f);
        });
    }

    private static SurfaceMapSet Blocks(string id, uint seed, int resolution, int columns, int rows,
        bool stagger, Vector3 color, float relief)
    {
        return SurfaceMapBaker.Bake(id, resolution, resolution, Vector2.One, uv =>
        {
            int row = (int)MathF.Floor(uv.Y * rows);
            float across = uv.X * columns;
            if (stagger && row % 2 == 1) { across += .5f; }
            int column = (int)MathF.Floor(across) % columns;
            float u = across - MathF.Floor(across);
            float v = uv.Y * rows - row;
            float edge = Math.Min(Math.Min(u, 1 - u) / columns, Math.Min(v, 1 - v) / rows);
            float face = Smooth(.003f, .012f, edge);
            float grain = Noise(uv, 96, seed + 37);
            float variation = .82f + Hash(column, row % rows, seed) * .36f;
            float crown = MathF.Sin(u * MathF.PI) * MathF.Sin(v * MathF.PI) * relief * .12f;
            float height = -relief * (1 - face) + face * (crown + (grain - .5f) * .00055f);
            Vector3 brick = color * variation * (.92f + grain * .16f);
            Vector3 mortar = new(.025f, .027f, .029f);
            Vector3 albedo = Vector3.Lerp(mortar, brick, face);
            return new(height, albedo, .72f + grain * .18f, Cavity: .68f + face * .32f);
        });
    }

    private static float Noise(Vector2 uv, int period, uint seed)
    {
        Vector2 cell = uv * period;
        int x = (int)MathF.Floor(cell.X);
        int y = (int)MathF.Floor(cell.Y);
        float u = Smooth(0, 1, cell.X - x);
        float v = Smooth(0, 1, cell.Y - y);
        float a = Hash(x % period, y % period, seed);
        float b = Hash((x + 1) % period, y % period, seed);
        float c = Hash(x % period, (y + 1) % period, seed);
        float d = Hash((x + 1) % period, (y + 1) % period, seed);
        return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
    }

    private static float Hash(int x, int y, uint seed)
    {
        uint bits = unchecked((uint)x * 374761393 + (uint)y * 668265263 + seed * 2246822519);
        bits = unchecked((bits ^ (bits >> 13)) * 1274126177);
        bits ^= bits >> 16;
        return (bits & 0xffffff) / 16777215f;
    }

    private static float Smooth(float low, float high, float value)
    {
        float t = Math.Clamp((value - low) / (high - low), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
