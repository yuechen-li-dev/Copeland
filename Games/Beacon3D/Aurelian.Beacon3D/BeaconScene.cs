using Aurelian.Runtime;
using static Aurelian.Graphics.Vulkan.Native3D.PrimitiveGeometry3D;
using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.World.Scenes;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Beacon3D;

internal static class BeaconScene
{
    public static SceneGroup Arena()
    {
        var children = new List<SceneNode>();
        var floor = new List<Native3DVertex>();
        for (int x = -11; x < 11; x += 2)
        {
            for (int z = -11; z < 11; z += 2)
            {
                bool alternate = ((x + z) / 2) % 2 == 0;
                Vector4 color = alternate ? new(0.18f, 0.26f, 0.30f, 1) : new(0.22f, 0.31f, 0.34f, 1);
                AddQuad(floor, new(x, 0, z), new(x + 2, 0, z), new(x + 2, 0, z + 2), new(x, 0, z + 2), Vector3.UnitY, color);
            }
        }
        children.Add(Scene.Mesh("floor", floor.Select(vertex => new SceneVertex(vertex.Position, vertex.Normal, vertex.Color)))
            with { Collision = SceneCollision.Solid });
        Vector4 wallColor = new(0.35f, 0.46f, 0.51f, 1);
        var sideWall = Scene.Group("side-wall",
            [Scene.Box("body", new(0.8f, 1, 23.6f), wallColor, collision: SceneCollision.Solid)]);
        var endWall = Scene.Group("end-wall",
            [Scene.Box("body", new(22, 1, 0.8f), wallColor, collision: SceneCollision.Solid)]);
        children.Add(Scene.Instance("west", sideWall, at: new(-11.4f, 0.5f, 0)));
        children.Add(Scene.Instance("east", sideWall, at: new(11.4f, 0.5f, 0)));
        children.Add(Scene.Instance("north", endWall, at: new(0, 0.5f, -11.4f)));
        children.Add(Scene.Instance("south", endWall, at: new(0, 0.5f, 11.4f)));
        for (int index = 0; index < BeaconGame.Pillars.Count; index++)
        {
            ArenaPillar pillar = BeaconGame.Pillars[index];
            children.Add(Scene.Group($"pillar-{index}",
            [
                Scene.Box("body", new(pillar.HalfSize.X * 2, pillar.Height, pillar.HalfSize.Y * 2),
                    new(0.48f, 0.59f, 0.62f, 1), at: new(0, pillar.Height / 2, 0), collision: SceneCollision.Solid),
                Scene.Box("cap", new(pillar.HalfSize.X * 2 + 0.3f, 0.16f, pillar.HalfSize.Y * 2 + 0.3f),
                    new(0.81f, 0.64f, 0.35f, 1), at: new(0, pillar.Height + 0.08f, 0), collision: SceneCollision.Solid),
            ], at: new(pillar.Center.X, 0, pillar.Center.Y)));
        }
        children.Add(Scene.Box("lamp", new(3, .12f, 1.5f), new(1, .8f, .45f, 1), at: new(.5f, 6, -3)) with
        {
            Material = new ModelMaterial("beacon-lamp")
            {
                BaseColor = Vector4.One, Metallic = 0, Roughness = 1, DoubleSided = true,
                Emissive = BeaconLighting.EmitterRadiance,
            },
        });
        var shelter = BeaconEnvironment.Shelter;
        Vector3 roofSize = shelter.Maximum - shelter.Minimum;
        Vector3 roofCenter = (shelter.Maximum + shelter.Minimum) * .5f;
        children.Add(Scene.Box("spawn-canopy", new(roofSize.X, .15f, roofSize.Z), new(.22f, .3f, .33f, 1),
            at: new(roofCenter.X, shelter.Maximum.Y, roofCenter.Z), collision: SceneCollision.Solid));
        return Scene.World("beacon-arena", children);
    }

    public static Native3DVertex[] Build(BeaconGame game, FirstPersonPose3D? pose = null, bool includeViewmodel = true)
    {
        var geometry = new BeaconVertexGeometry();
        Describe(game, geometry, pose, includeViewmodel);
        return geometry.Vertices.ToArray();
    }

    internal static void Describe(BeaconGame game, BeaconGeometry geometry, FirstPersonPose3D? pose = null, bool includeViewmodel = true)
    {
        var frame = game.Scene.Project();
        foreach (var box in frame.Boxes)
        {
            geometry.Box("arena/" + box.Id, Vector3.Zero, box.HalfSize, box.Color, box.WorldTransform, box.Material);
        }
        foreach (var mesh in frame.Meshes)
        {
            geometry.Mesh(mesh);
        }
        if (game.HumanoidOptions is { } humanoid)
        {
            geometry.Deformed(SceneGeometry3D.Build(humanoid.Attach(game.CharacterPose!, game.CharacterWorld)));
        }
        for (int index = 0; index < BeaconGame.BeaconPositions.Count; index++)
        {
            Vector2 point = game.BeaconPosition(index);
            Vector4 color = game.IsCollected(index) ? new(0.16f, 0.35f, 0.33f, 1) : new(1, 0.65f, 0.16f, 1);
            geometry.Box($"beacon-{index}/base", new(point.X, 0.12f, point.Y), new(0.65f, 0.12f, 0.65f), color);
            if (!game.IsCollected(index))
            {
                geometry.Diamond($"beacon-{index}/diamond", new(point.X, 1.5f + 0.15f * MathF.Sin(game.Time * 2), point.Y), game.Time, color);
            }
        }
        foreach (var creature in game.Creatures)
        {
            Vector3 point = creature.State.Position;
            if (creature.State.Health == 0)
            {
                geometry.Box(creature.Id + "/remains", point + new Vector3(0, 0.12f, 0), new(0.5f, 0.12f, 0.5f), new(0.25f, 0.16f, 0.22f, 1));
                continue;
            }
            float bob = MathF.Sin(game.Time * 7 + point.X) * 0.08f;
            geometry.Box(creature.Id + "/body", point + new Vector3(0, 0.7f + bob, 0), new(0.45f, 0.6f, 0.45f), new(0.8f, 0.19f, 0.29f, 1));
            geometry.Diamond(creature.Id + "/diamond", point + new Vector3(0, 1.5f + bob, 0), game.Time * 0.5f, new(1, 0.38f, 0.25f, 1));
            Vector2 toward = game.Position - new Vector2(point.X, point.Z);
            if (toward.LengthSquared() > 0)
            {
                toward = Vector2.Normalize(toward);
            }
            geometry.Box(creature.Id + "/eye", point + new Vector3(toward.X * 0.48f, 1.25f + bob, toward.Y * 0.48f),
                new(0.12f, 0.12f, 0.12f), new(1, 0.85f, 0.3f, 1));
        }
        for (int index = 0; index < game.Bolts.Count; index++)
        {
            BeaconBolt bolt = game.Bolts[index];
            geometry.Box("bolt-" + index, bolt.Position, new(0.06f, 0.06f, 0.06f), new(0.4f, 1, 1, 1));
        }
        if (includeViewmodel)
        {
            DescribeViewmodel(game, geometry, pose ?? FirstPersonPose3D.Neutral);
        }
        Vector4 gateColor = game.GateOpen ? new(0.25f, 1, 0.65f, 1) : new(0.37f, 0.49f, 0.57f, 1);
        geometry.Box("gate/0", new(-1.4f, 1.5f, -10), new(0.2f, 1.5f, 0.25f), gateColor);
        geometry.Box("gate/1", new(1.4f, 1.5f, -10), new(0.2f, 1.5f, 0.25f), gateColor);
        geometry.Box("gate/2", new(0, 3, -10), new(1.6f, 0.2f, 0.25f), gateColor);
        geometry.Box("gate/3", new(0, 0.025f, -10), new(1.2f, 0.025f, 0.9f), gateColor);
    }

    public static Native3DVertex[] Viewmodel(BeaconGame game, FirstPersonPose3D pose)
    {
        var geometry = new BeaconVertexGeometry();
        DescribeViewmodel(game, geometry, pose);
        return geometry.Vertices.ToArray();
    }

    private static void DescribeViewmodel(BeaconGame game, BeaconGeometry geometry, FirstPersonPose3D view)
    {
        if (game.Won || game.Dead || game.HumanoidOptions is not null || game.View != CameraView.FirstPerson)
        {
            return;
        }
        Vector3 right = Vector3.Normalize(Vector3.Cross(game.Direction, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, game.Direction));
        Vector3 cameraEye = view.Eye(game.Eye, game.Direction);
        Vector3 gun = cameraEye + game.Direction * Math.Max(.20f, .62f + view.WeaponOffset.Z)
            + right * (.24f * (1 - view.Aim) + view.WeaponOffset.X)
            + up * (-.22f * (1 - view.Aim) - .07f * view.Aim + view.WeaponOffset.Y);
        Quaternion rotation = Quaternion.CreateFromAxisAngle(right, view.WeaponAngles.X)
            * Quaternion.CreateFromAxisAngle(up, view.WeaponAngles.Y)
            * Quaternion.CreateFromAxisAngle(game.Direction, view.WeaponAngles.Z);
        right = Vector3.Transform(right, rotation);
        up = Vector3.Transform(up, rotation);
        Vector3 barrel = Vector3.Transform(game.Direction, rotation);
        geometry.ViewBox("view/weapon", gun, right, up, barrel, new(0.055f, 0.06f, 0.16f), new(0.3f, 0.55f, 0.6f, 1));
        geometry.ViewBox("view/barrel", gun + barrel * .22f, right, up, barrel, new(.022f, .024f, .10f), new(.10f, .16f, .18f, 1));
        geometry.ViewBox("view/sight", gun + up * .073f, right, up, barrel, new(.012f, .012f, .045f), new(.45f, .95f, .8f, 1));
        geometry.ViewBox("view/grip", gun - up * .095f - barrel * .035f, right, up, barrel, new(.035f, .07f, .045f), new(.12f, .2f, .22f, 1));
        if (game.Bolts.Count > 0 && game.Bolts[^1].Life > 1.45f)
        {
            geometry.Diamond("view/flash", gun + barrel * .33f, game.Time, new(0.7f, 1, 1, 1), 0.12f);
        }
    }

    public static void AddBox(List<Native3DVertex> vertices, Vector3 center, Vector3 halfSize, Vector4 color)
    {
        PrimitiveGeometry3D.AddBox(vertices, center, halfSize, color);
    }

    internal static void AddViewBox(List<Native3DVertex> vertices, Vector3 center, Vector3 right, Vector3 up,
        Vector3 forward, Vector3 halfSize, Vector4 color)
    {
        var local = new List<Native3DVertex>();
        AddBox(local, Vector3.Zero, halfSize, color);
        foreach (Native3DVertex vertex in local)
        {
            Vector3 position = center + right * vertex.Position.X + up * vertex.Position.Y + forward * vertex.Position.Z;
            Vector3 normal = right * vertex.Normal.X + up * vertex.Normal.Y + forward * vertex.Normal.Z;
            vertices.Add(new Native3DVertex(position, normal, vertex.Color));
        }
    }

    internal static void AddDiamond(List<Native3DVertex> vertices, Vector3 center, float rotation, Vector4 color, float scale = 1)
    {
        Vector3 top = center + Vector3.UnitY * (0.7f * scale);
        Vector3 bottom = center - Vector3.UnitY * (0.7f * scale);
        for (int side = 0; side < 4; side++)
        {
            float angle = rotation + side * MathF.PI / 2;
            Vector3 a = center + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * (0.45f * scale);
            Vector3 b = center + new Vector3(MathF.Cos(angle + MathF.PI / 2), 0, MathF.Sin(angle + MathF.PI / 2)) * (0.45f * scale);
            AddTriangle(vertices, top, b, a, Vector3.Normalize(Vector3.Cross(b - top, a - top)), color);
            AddTriangle(vertices, bottom, a, b, Vector3.Normalize(Vector3.Cross(a - bottom, b - bottom)), color);
        }
    }

}
