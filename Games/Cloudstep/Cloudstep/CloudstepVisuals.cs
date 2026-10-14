using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

namespace Cloudstep;

/// <summary>Procedural placeholder art: the hero, gems, flags, thorns and course blocks, all from code.</summary>
internal static class CloudstepVisuals
{
    public static readonly Vector4 Grass = Rgb(0x4CAF2E);
    public static readonly Vector4 GrassEdge = Rgb(0x4E9A3A);
    public static readonly Vector4 Earth = Rgb(0x7A4A2E);
    public static readonly Vector4 Stone = Rgb(0xB9B2A6);
    public static readonly Vector4 StoneDark = Rgb(0x8C867C);
    public static readonly Vector4 Wood = Rgb(0xB07A45);
    public static readonly Vector4 WoodTop = Rgb(0xD39B5C);
    public static readonly Vector4 Cloud = Rgb(0xC9DBF0);
    public static readonly Vector4 Hero = Rgb(0xFF5A36);
    public static readonly Vector4 HeroBelly = Rgb(0xFFD3B5);

    public static readonly ImmutableArray<SceneVertex> HeroBody = Capsule(0.35f, 1.4f, 16, 8, Hero, HeroBelly);
    public static readonly ImmutableArray<SceneVertex> Gem = Octahedron(0.32f, 0.5f, new(0.55f, 0.95f, 1, 1));
    public static readonly ImmutableArray<SceneVertex> Thorn = Pyramid(0.22f, 0.5f, Rgb(0x5B2333));

    public static readonly ModelMaterial GemMaterial = new("gem")
    {
        BaseColor = new(0.45f, 0.9f, 1, 1),
        Metallic = 0,
        Roughness = 0.15f,
        Emissive = new(0.6f, 1.6f, 2.2f),
    };

    public static readonly ModelMaterial BeaconMaterial = new("beacon")
    {
        BaseColor = new(1, 0.85f, 0.4f, 1),
        Metallic = 0,
        Roughness = 0.3f,
        Emissive = new(3, 2.2f, 0.8f),
    };

    public static readonly ModelMaterial ActiveFlagMaterial = new("flag-active")
    {
        BaseColor = new(1, 0.8f, 0.25f, 1),
        Metallic = 0,
        Roughness = 0.5f,
        Emissive = new(1.2f, 0.8f, 0.15f),
    };

    public static Vector4 Rgb(uint hex) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1);

    /// <summary>Course blocks: grass islands get a green cap over earth, other kinds a lighter top slab.</summary>
    public static void AddBlock(List<PlacedSceneBox> boxes, CourseBlock block)
    {
        (Vector4 top, Vector4 body) = block.Kind switch
        {
            SurfaceKind.Grass => (Grass, Earth),
            SurfaceKind.Stone => (Stone, StoneDark),
            SurfaceKind.Wood => (WoodTop, Wood),
            _ => (Cloud, Cloud),
        };
        if (block.Kind == SurfaceKind.Cloud)
        {
            boxes.Add(new PlacedSceneBox(block.Id, block.Transform, block.HalfSize, body, SceneCollision.None));
            return;
        }
        AddCapped(boxes, block.Id, block.Transform, block.HalfSize, top, body, block.Kind == SurfaceKind.Grass ? 0.22f : 0.1f);
    }

    /// <summary>A box drawn as a thin top slab over a body, both in the block's local frame.</summary>
    public static void AddCapped(List<PlacedSceneBox> boxes, string id, Matrix4x4 transform, Vector3 half, Vector4 top, Vector4 body,
        float capThickness)
    {
        float cap = MathF.Min(capThickness, half.Y);
        var capHalf = new Vector3(half.X + 0.02f, cap / 2, half.Z + 0.02f);
        var bodyHalf = new Vector3(half.X, half.Y - cap / 2, half.Z);
        boxes.Add(new PlacedSceneBox(id + ".cap", Matrix4x4.CreateTranslation(0, half.Y - cap / 2, 0) * transform, capHalf, top,
            SceneCollision.None));
        if (bodyHalf.Y > 0.001f)
        {
            boxes.Add(new PlacedSceneBox(id + ".body", Matrix4x4.CreateTranslation(0, -cap / 2, 0) * transform, bodyHalf, body,
                SceneCollision.None));
        }
    }

    /// <summary>The hero: a rounded capsule with eyes and a leaf, squashed and stretched about its feet.</summary>
    public static void AddHero(List<PlacedSceneBox> boxes, List<PlacedSceneMesh> meshes, Vector3 feet, float facing, float squash,
        float alpha)
    {
        if (alpha <= 0.01f)
        {
            return;
        }
        float wide = 1 + squash;
        float tall = 1 / (wide * wide);
        Matrix4x4 frame = Matrix4x4.CreateScale(wide, tall, wide) * Matrix4x4.CreateRotationY(-facing) * Matrix4x4.CreateTranslation(feet);
        meshes.Add(new PlacedSceneMesh("hero.body", frame, HeroBody) { GeometryIdentity = "cloudstep.hero.v1" });
        // Eyes look along local -Z (forward).
        foreach (float side in new[] { -1f, 1f })
        {
            boxes.Add(new PlacedSceneBox($"hero.eye{side}", Matrix4x4.CreateTranslation(side * 0.13f, 1.0f, -0.31f) * frame,
                new(0.075f, 0.11f, 0.04f), new(1, 1, 1, 1), SceneCollision.None));
            boxes.Add(new PlacedSceneBox($"hero.pupil{side}", Matrix4x4.CreateTranslation(side * 0.13f, 0.98f, -0.345f) * frame,
                new(0.04f, 0.065f, 0.02f), Rgb(0x1E1A2E), SceneCollision.None));
        }
        boxes.Add(new PlacedSceneBox("hero.leaf", Matrix4x4.CreateRotationZ(0.5f) * Matrix4x4.CreateTranslation(0.06f, 1.47f, 0) * frame,
            new(0.12f, 0.03f, 0.06f), Rgb(0x58C35B), SceneCollision.None));
        boxes.Add(new PlacedSceneBox("hero.stem", Matrix4x4.CreateTranslation(0, 1.42f, 0) * frame,
            new(0.02f, 0.05f, 0.02f), Rgb(0x3F8F3F), SceneCollision.None));
    }

    public static void AddFlag(List<PlacedSceneBox> boxes, string id, Vector3 foot, bool active, float time)
    {
        boxes.Add(new PlacedSceneBox(id + ".pole", Matrix4x4.CreateTranslation(foot + new Vector3(0, 1.2f, 0)), new(0.05f, 1.2f, 0.05f),
            Rgb(0xE8E2D6), SceneCollision.None));
        float wave = MathF.Sin(time * 4) * 0.12f;
        var flag = new PlacedSceneBox(id + ".flag",
            Matrix4x4.CreateRotationY(wave) * Matrix4x4.CreateTranslation(foot + new Vector3(0.42f, 2.05f, 0)), new(0.38f, 0.24f, 0.02f),
            active ? new Vector4(1, 0.8f, 0.25f, 1) : Rgb(0x8E9AAF), SceneCollision.None)
        {
            Material = active ? ActiveFlagMaterial : null,
        };
        boxes.Add(flag);
        boxes.Add(new PlacedSceneBox(id + ".base", Matrix4x4.CreateTranslation(foot + new Vector3(0, 0.06f, 0)), new(0.35f, 0.06f, 0.35f),
            Stone, SceneCollision.None));
    }

    public static void AddBeacon(List<PlacedSceneBox> boxes, Vector3 foot, float time, bool reached)
    {
        boxes.Add(new PlacedSceneBox("goal.plinth", Matrix4x4.CreateTranslation(foot + new Vector3(0, 0.15f, 0)), new(1.3f, 0.15f, 1.3f),
            Stone, SceneCollision.None));
        float height = reached ? 7 : 4.5f;
        boxes.Add(new PlacedSceneBox("goal.column", Matrix4x4.CreateRotationY(time * 0.8f) * Matrix4x4.CreateTranslation(foot + new Vector3(0, 0.3f + height / 2, 0)),
            new(0.18f, height / 2, 0.18f), new(1, 0.85f, 0.4f, 1), SceneCollision.None) { Material = BeaconMaterial });
        for (int index = 0; index < 6; index++)
        {
            float angle = time * 1.2f + index * MathF.Tau / 6;
            Vector3 orbit = foot + new Vector3(MathF.Cos(angle) * 1.1f, 1.4f + MathF.Sin(time * 2 + index) * 0.25f, MathF.Sin(angle) * 1.1f);
            boxes.Add(new PlacedSceneBox($"goal.spark{index}", Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(orbit),
                new(0.09f, 0.09f, 0.09f), new(1, 0.85f, 0.4f, 1), SceneCollision.None) { Material = BeaconMaterial });
        }
    }

    public static void AddGem(List<PlacedSceneMesh> meshes, int index, Vector3 position, float time)
    {
        float bob = MathF.Sin(time * 2.5f + index) * 0.12f;
        Matrix4x4 frame = Matrix4x4.CreateRotationY(time * 2 + index * 0.7f) * Matrix4x4.CreateTranslation(position + new Vector3(0, bob, 0));
        meshes.Add(new PlacedSceneMesh($"gem.{index}", frame, Gem) { Material = GemMaterial, GeometryIdentity = "cloudstep.gem.v1" });
    }

    public static void AddThorns(List<PlacedSceneBox> boxes, List<PlacedSceneMesh> meshes, CourseHazard hazard)
    {
        Vector3 size = hazard.Max - hazard.Min;
        boxes.Add(new PlacedSceneBox(hazard.Id + ".bed", Matrix4x4.CreateTranslation(new Vector3((hazard.Min.X + hazard.Max.X) / 2,
            hazard.Min.Y + 0.04f, (hazard.Min.Z + hazard.Max.Z) / 2)), new(size.X / 2, 0.04f, size.Z / 2), Rgb(0x3A1D24), SceneCollision.None));
        int across = Math.Max(1, (int)(size.X / 0.5f));
        int deep = Math.Max(1, (int)(size.Z / 0.5f));
        for (int x = 0; x < across; x++)
        {
            for (int z = 0; z < deep; z++)
            {
                var at = new Vector3(hazard.Min.X + (x + 0.5f) * size.X / across, hazard.Min.Y, hazard.Min.Z + (z + 0.5f) * size.Z / deep);
                meshes.Add(new PlacedSceneMesh($"{hazard.Id}.{x}.{z}", Matrix4x4.CreateTranslation(at), Thorn)
                {
                    GeometryIdentity = "cloudstep.thorn.v1",
                });
            }
        }
    }

    private static ImmutableArray<SceneVertex> Capsule(float radius, float height, int segments, int rings, Vector4 back, Vector4 front)
    {
        var vertices = new List<SceneVertex>();
        float straight = height - 2 * radius;
        // Rows of (y, ring radius, normal Y) from the bottom pole to the top pole.
        var rows = new List<(float Y, float R, float NormalY, float NormalR)>();
        for (int ring = 0; ring <= rings; ring++)
        {
            float angle = -MathF.PI / 2 + ring * (MathF.PI / 2) / rings;
            rows.Add((radius + MathF.Sin(angle) * radius, MathF.Cos(angle) * radius, MathF.Sin(angle), MathF.Cos(angle)));
        }
        for (int ring = 0; ring <= rings; ring++)
        {
            float angle = ring * (MathF.PI / 2) / rings;
            rows.Add((radius + straight + MathF.Sin(angle) * radius, MathF.Cos(angle) * radius, MathF.Sin(angle), MathF.Cos(angle)));
        }
        for (int row = 0; row < rows.Count - 1; row++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                float a0 = segment * MathF.Tau / segments;
                float a1 = (segment + 1) * MathF.Tau / segments;
                SceneVertex Vertex(int r, float a)
                {
                    var (y, ringRadius, ny, nr) = rows[r];
                    var direction = new Vector3(MathF.Sin(a), 0, -MathF.Cos(a));
                    // The belly faces forward (-Z); it blends into the back colour around the sides.
                    float facing = Math.Clamp(-direction.Z * 1.4f - 0.2f, 0, 1) * Math.Clamp((y - 0.25f) / 0.5f, 0, 1)
                        * Math.Clamp((1.05f - y) / 0.3f, 0, 1);
                    return new SceneVertex(direction * ringRadius + new Vector3(0, y, 0),
                        Vector3.Normalize(direction * nr + new Vector3(0, ny, 0)), Vector4.Lerp(back, front, facing));
                }
                SceneVertex p00 = Vertex(row, a0);
                SceneVertex p01 = Vertex(row, a1);
                SceneVertex p10 = Vertex(row + 1, a0);
                SceneVertex p11 = Vertex(row + 1, a1);
                vertices.AddRange([p00, p10, p11, p00, p11, p01]);
            }
        }
        return [.. vertices];
    }

    private static ImmutableArray<SceneVertex> Octahedron(float width, float height, Vector4 color)
    {
        Vector3 top = new(0, height, 0);
        Vector3 bottom = new(0, -height, 0);
        Vector3[] ring = [new(width, 0, 0), new(0, 0, -width), new(-width, 0, 0), new(0, 0, width)];
        var vertices = new List<SceneVertex>();
        for (int index = 0; index < 4; index++)
        {
            Vector3 a = ring[index];
            Vector3 b = ring[(index + 1) % 4];
            AddFacet(vertices, a, b, top, color);
            AddFacet(vertices, b, a, bottom, color);
        }
        return [.. vertices];
    }

    private static ImmutableArray<SceneVertex> Pyramid(float half, float height, Vector4 color)
    {
        Vector3 apex = new(0, height, 0);
        Vector3[] ring = [new(half, 0, half), new(half, 0, -half), new(-half, 0, -half), new(-half, 0, half)];
        var vertices = new List<SceneVertex>();
        for (int index = 0; index < 4; index++)
        {
            AddFacet(vertices, ring[index], ring[(index + 1) % 4], apex, color);
        }
        return [.. vertices];
    }

    private static void AddFacet(List<SceneVertex> vertices, Vector3 a, Vector3 b, Vector3 c, Vector4 color)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        // Keep the facet facing away from the shape's centre.
        if (Vector3.Dot(normal, (a + b + c) / 3) < 0)
        {
            (a, b) = (b, a);
            normal = -normal;
        }
        vertices.Add(new SceneVertex(a, normal, color));
        vertices.Add(new SceneVertex(b, normal, color));
        vertices.Add(new SceneVertex(c, normal, color));
    }
}
