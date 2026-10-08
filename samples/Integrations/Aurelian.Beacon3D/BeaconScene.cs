using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;

namespace Aurelian.Beacon3D;

internal static class BeaconScene
{
    public static Native3DVertex[] Build(BeaconGame game)
    {
        var vertices = new List<Native3DVertex>();
        for (int x = -11; x < 11; x += 2)
        {
            for (int z = -11; z < 11; z += 2)
            {
                bool alternate = ((x + z) / 2) % 2 == 0;
                Vector4 color = alternate ? new(0.18f, 0.26f, 0.30f, 1) : new(0.22f, 0.31f, 0.34f, 1);
                AddQuad(vertices, new(x, 0, z), new(x + 2, 0, z), new(x + 2, 0, z + 2), new(x, 0, z + 2), Vector3.UnitY, color);
            }
        }
        Vector4 wallColor = new(0.35f, 0.46f, 0.51f, 1);
        AddBox(vertices, new(-11.4f, 0.5f, 0), new(0.4f, 0.5f, 11.8f), wallColor);
        AddBox(vertices, new(11.4f, 0.5f, 0), new(0.4f, 0.5f, 11.8f), wallColor);
        AddBox(vertices, new(0, 0.5f, -11.4f), new(11, 0.5f, 0.4f), wallColor);
        AddBox(vertices, new(0, 0.5f, 11.4f), new(11, 0.5f, 0.4f), wallColor);
        foreach (ArenaPillar pillar in BeaconGame.Pillars)
        {
            AddBox(vertices, new(pillar.Center.X, pillar.Height / 2, pillar.Center.Y),
                new(pillar.HalfSize.X, pillar.Height / 2, pillar.HalfSize.Y), new(0.48f, 0.59f, 0.62f, 1));
            AddBox(vertices, new(pillar.Center.X, pillar.Height + 0.08f, pillar.Center.Y),
                new(pillar.HalfSize.X + 0.15f, 0.08f, pillar.HalfSize.Y + 0.15f), new(0.81f, 0.64f, 0.35f, 1));
        }
        for (int index = 0; index < BeaconGame.BeaconPositions.Count; index++)
        {
            Vector2 point = BeaconGame.BeaconPositions[index];
            Vector4 color = game.IsCollected(index) ? new(0.16f, 0.35f, 0.33f, 1) : new(1, 0.65f, 0.16f, 1);
            AddBox(vertices, new(point.X, 0.12f, point.Y), new(0.65f, 0.12f, 0.65f), color);
            if (!game.IsCollected(index))
            {
                AddDiamond(vertices, new(point.X, 1.5f + 0.15f * MathF.Sin(game.Time * 2), point.Y), game.Time, color);
            }
        }
        Vector4 gateColor = game.CollectedCount == 3 ? new(0.25f, 1, 0.65f, 1) : new(0.37f, 0.49f, 0.57f, 1);
        AddBox(vertices, new(-1.4f, 1.5f, -10), new(0.2f, 1.5f, 0.25f), gateColor);
        AddBox(vertices, new(1.4f, 1.5f, -10), new(0.2f, 1.5f, 0.25f), gateColor);
        AddBox(vertices, new(0, 3, -10), new(1.6f, 0.2f, 0.25f), gateColor);
        AddBox(vertices, new(0, 0.025f, -10), new(1.2f, 0.025f, 0.9f), gateColor);
        return vertices.ToArray();
    }

    public static void AddBox(List<Native3DVertex> vertices, Vector3 center, Vector3 halfSize, Vector4 color)
    {
        Vector3 min = center - halfSize;
        Vector3 max = center + halfSize;
        AddQuad(vertices, new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, max.Z), new(min.X, max.Y, min.Z), -Vector3.UnitX, color);
        AddQuad(vertices, new(max.X, min.Y, max.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), Vector3.UnitX, color);
        AddQuad(vertices, new(min.X, max.Y, min.Z), new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z), new(max.X, max.Y, min.Z), Vector3.UnitY, color);
        AddQuad(vertices, new(min.X, min.Y, max.Z), new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), -Vector3.UnitY, color);
        AddQuad(vertices, new(max.X, min.Y, min.Z), new(min.X, min.Y, min.Z), new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), -Vector3.UnitZ, color);
        AddQuad(vertices, new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z), Vector3.UnitZ, color);
    }

    private static void AddDiamond(List<Native3DVertex> vertices, Vector3 center, float rotation, Vector4 color)
    {
        Vector3 top = center + Vector3.UnitY * 0.7f;
        Vector3 bottom = center - Vector3.UnitY * 0.7f;
        for (int side = 0; side < 4; side++)
        {
            float angle = rotation + side * MathF.PI / 2;
            Vector3 a = center + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * 0.45f;
            Vector3 b = center + new Vector3(MathF.Cos(angle + MathF.PI / 2), 0, MathF.Sin(angle + MathF.PI / 2)) * 0.45f;
            AddTriangle(vertices, top, b, a, Vector3.Normalize(Vector3.Cross(b - top, a - top)), color);
            AddTriangle(vertices, bottom, a, b, Vector3.Normalize(Vector3.Cross(a - bottom, b - bottom)), color);
        }
    }

    private static void AddQuad(List<Native3DVertex> vertices, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector4 color)
    {
        AddTriangle(vertices, a, b, c, normal, color);
        AddTriangle(vertices, a, c, d, normal, color);
    }

    private static void AddTriangle(List<Native3DVertex> vertices, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector4 color)
    {
        vertices.Add(new(a, normal, color));
        vertices.Add(new(b, normal, color));
        vertices.Add(new(c, normal, color));
    }
}
