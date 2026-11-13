using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Assets.Materials;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

namespace Aurelian.Beacon3D;

public static class BeaconEnvironment
{
    public static readonly RainShelter3D Shelter = new(new(-2.75f, -.1f, 6.8f), new(2.75f, 3.25f, 11), .5f);
    public static readonly WetGroundField3D Ground = BuildGround();
    private static readonly Lazy<Native3DScene> Surface = new(BuildSurface);

    public static WetGroundSample3D SurfaceAt(Vector2 position)
    {
        Vector2 uv = (position - Ground.Origin) / Ground.Size;
        if (uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1)
        {
            return new(Ground.WaterLevel, 0, 1);
        }
        return Ground.Sample(position);
    }

    public static Native3DScene Present(Native3DScene arena, float time, IReadOnlyList<WaterImpulse3D> impacts)
    {
        return arena with
        {
            Models = arena.Models.Concat(Surface.Value.Models).ToArray(),
            WaterReflection = new(Ground.WaterLevel),
            WaterFrame = new(time) { Impulses = impacts.ToImmutableArray() },
        };
    }

    private static WetGroundField3D BuildGround()
    {
        const int nodes = 65;
        var samples = new List<WetGroundSample3D>();
        for (int z = 0; z < nodes; z++)
        {
            for (int x = 0; x < nodes; x++)
            {
                Vector2 p = new(-11 + 22f * x / (nodes - 1), -11 + 22f * z / (nodes - 1));
                Vector2 basin = (p - new Vector2(-5, 5)) / new Vector2(1.8f, 1.7f);
                float height = Math.Min(.045f, .018f + .014f * (basin.LengthSquared() - 1));
                float exposure = 1 - Shelter.ShelterAt(new(p.X, height, p.Y));
                samples.Add(WetGroundSample3D.FromHeight(height, .018f, .85f, exposure, .012f));
            }
        }
        return new(new(-11), new(22), nodes, nodes, .018f, samples);
    }

    private static Native3DScene BuildSurface()
    {
        ModelMaterial asphalt = ProceduralSurfaceMaps.Asphalt("beacon-dry-asphalt", 0, 733).Material("beacon-ground")
            with { Metallic = 0, SpecularAntiAliasing = true };
        var (ground, water) = WetGroundMesh3D.Create("beacon-weather", Ground, asphalt,
            new WaterSurface3D { DetailAmplitude = .0005f, FoamStrength = 0, MaximumDepth = .1f });
        var nodes = new List<SceneNode> { Scene.Model("ground", ground, Vector3.Zero) };
        if (water is not null)
        {
            nodes.Add(Scene.Model("puddle", water, Vector3.Zero));
        }
        using var scene = SceneCompiler.Compile(Scene.World("beacon-surfaces", nodes)).Mount();
        return SceneGeometry3D.BuildScene(scene.Project());
    }
}
