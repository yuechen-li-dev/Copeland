using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.World.Scenes;

/// <summary>Ground and clipped water geometry from one field. Vertex alpha carries opt-in wetness, not opacity.</summary>
public static class WetGroundMesh3D
{
    public static (ModelSlot Ground, ModelSlot? Water) Create(string id, WetGroundField3D field,
        ModelMaterial dryMaterial, WaterSurface3D water, Vector2? uvTileSizeMetres = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(dryMaterial);
        ArgumentNullException.ThrowIfNull(water);
        water.Validate();
        Vector2 tile = uvTileSizeMetres ?? Vector2.One;
        if (string.IsNullOrWhiteSpace(id) || !float.IsFinite(tile.X) || !float.IsFinite(tile.Y)
            || tile.X < .01f || tile.Y < .01f || tile.X > 1000 || tile.Y > 1000)
        {
            throw new ArgumentException("Ground needs an identity and positive finite UV tile dimensions.");
        }
        var groundVertices = new List<ModelVertex>();
        var waterVertices = new List<ModelVertex>();
        for (int row = 0; row < field.Rows - 1; row++)
        {
            for (int column = 0; column < field.Columns - 1; column++)
            {
                GroundVertex a = Vertex(column, row);
                GroundVertex b = Vertex(column, row + 1);
                GroundVertex c = Vertex(column + 1, row);
                GroundVertex d = Vertex(column + 1, row + 1);
                Triangle(a, b, d);
                Triangle(a, d, c);
            }
        }
        var ground = Build(id, groundVertices, dryMaterial with { VertexWetness = true });
        ModelSlot? standingWater = null;
        if (waterVertices.Count > 0)
        {
            standingWater = Build(id + "-water", waterVertices, water.Material());
        }
        return (ground, standingWater);

        GroundVertex Vertex(int column, int row)
        {
            Vector2 xz = field.Position(column, row);
            var sample = field.Samples[row * field.Columns + column];
            return new(new(xz.X, sample.Height, xz.Y), sample.Wetness);
        }

        void Triangle(GroundVertex a, GroundVertex b, GroundVertex c)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b.Position - a.Position, c.Position - a.Position));
            Vector3 tangent = Vector3.Normalize(new Vector3(1, -normal.X / normal.Y, 0));
            foreach (var vertex in new[] { a, b, c })
            {
                Vector3 p = vertex.Position;
                groundVertices.Add(new(p, normal, new Vector2(p.X, p.Z) / tile,
                    new(tangent, -1), new(1, 1, 1, vertex.Wetness)));
            }
            // Clip the same triangles used by CPU queries; no independent puddle masks.
            var polygon = new List<Vector3>();
            Vector3 previous = c.Position;
            float previousDepth = field.WaterLevel - previous.Y - .001f;
            foreach (var vertex in new[] { a, b, c })
            {
                Vector3 current = vertex.Position;
                float depth = field.WaterLevel - current.Y - .001f;
                if ((depth > 0) != (previousDepth > 0))
                {
                    polygon.Add(Vector3.Lerp(previous, current, previousDepth / (previousDepth - depth)));
                }
                if (depth > 0)
                {
                    polygon.Add(current);
                }
                previous = current;
                previousDepth = depth;
            }
            for (int index = 1; index + 1 < polygon.Count; index++)
            {
                foreach (Vector3 p in new[] { polygon[0], polygon[index], polygon[index + 1] })
                {
                    waterVertices.Add(new(new(p.X, field.WaterLevel, p.Z), Vector3.UnitY,
                        new Vector2(p.X, p.Z) / tile, new(1, 0, 0, -1), Vector4.One));
                }
            }
        }
    }

    private static ModelSlot Build(string id, List<ModelVertex> vertices, ModelMaterial material)
    {
        material.Validate();
        var primitive = new ModelPrimitive(id, vertices.ToImmutableArray(), Enumerable.Range(0, vertices.Count).ToImmutableArray(), material);
        string identity = ScenePlan.ComputePrimitiveModelIdentity(primitive);
        return new(new(id, identity, [new(id, primitive, Matrix4x4.Identity)]));
    }

    private readonly record struct GroundVertex(Vector3 Position, float Wetness);
}
