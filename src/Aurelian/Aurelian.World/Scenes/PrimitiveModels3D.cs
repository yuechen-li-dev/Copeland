using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.World.Scenes;

/// <summary>Small presentation meshes with metre-scaled UVs and explicit tangent frames.
/// Results use the ordinary Scene.Model path. They do not imply CAD topology or collision.</summary>
public static class PrimitiveModels3D
{
    public static ModelSlot Plane(string id, Vector2 size, ModelMaterial material, Vector2? uvTileSizeMetres = null)
    {
        Positive(size.X);
        Positive(size.Y);
        var mesh = new Mesh(uvTileSizeMetres);
        mesh.Vertex(new(-size.X / 2, 0, -size.Y / 2), Vector3.UnitY, new(0, 0), new(1, 0, 0, -1));
        mesh.Vertex(new(size.X / 2, 0, -size.Y / 2), Vector3.UnitY, new(size.X, 0), new(1, 0, 0, -1));
        mesh.Vertex(new(size.X / 2, 0, size.Y / 2), Vector3.UnitY, size, new(1, 0, 0, -1));
        mesh.Vertex(new(-size.X / 2, 0, size.Y / 2), Vector3.UnitY, new(0, size.Y), new(1, 0, 0, -1));
        mesh.Triangle(0, 2, 1);
        mesh.Triangle(0, 3, 2);
        return mesh.Build(id, material);
    }

    public static ModelSlot Box(string id, Vector3 size, ModelMaterial material, Vector2? uvTileSizeMetres = null)
    {
        return BeveledBox(id, size, 0, material, uvTileSizeMetres: uvTileSizeMetres);
    }

    /// <summary>Rounded edge strips on a box. Bevel is a radius in metres, below every half extent.</summary>
    public static ModelSlot BeveledBox(string id, Vector3 size, float bevel, ModelMaterial material,
        int bevelSegments = 3, Vector2? uvTileSizeMetres = null)
    {
        Positive(size.X);
        Positive(size.Y);
        Positive(size.Z);
        if (!float.IsFinite(bevel) || bevel < 0 || bevel >= Math.Min(size.X, Math.Min(size.Y, size.Z)) / 2
            || bevelSegments is < 1 or > 8)
        {
            throw new ArgumentException("AUR-PRIMITIVE-001: Bevel must be finite, nonnegative and smaller than each half extent; use 1..8 edge segments.");
        }
        var mesh = new Mesh(uvTileSizeMetres);
        Vector3 half = size / 2;
        Vector3 core = half - new Vector3(bevel);
        Face(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY);
        Face(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
        Face(Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ);
        Face(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
        Face(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        Face(-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY);
        return mesh.Build(id, material);

        void Face(Vector3 normal, Vector3 right, Vector3 up)
        {
            float extent = Vector3.Dot(Vector3.Abs(normal), half);
            float width = Vector3.Dot(Vector3.Abs(right), half);
            float height = Vector3.Dot(Vector3.Abs(up), half);
            float[] across = Coordinates(width, bevel, bevelSegments);
            float[] along = Coordinates(height, bevel, bevelSegments);
            int first = mesh.Count;
            foreach (float y in along)
            {
                foreach (float x in across)
                {
                    Vector3 position = normal * extent + right * x + up * y;
                    Vector3 shadedNormal = normal;
                    if (bevel > 0)
                    {
                        Vector3 closest = Vector3.Clamp(position, -core, core);
                        shadedNormal = Vector3.Normalize(position - closest);
                        position = closest + shadedNormal * bevel;
                    }
                    Vector3 tangent = Vector3.Normalize(right - shadedNormal * Vector3.Dot(right, shadedNormal));
                    mesh.Vertex(position, shadedNormal, new(x + width, y + height), new(tangent, 1));
                }
            }
            for (int y = 0; y < along.Length - 1; y++)
            {
                for (int x = 0; x < across.Length - 1; x++)
                {
                    int a = first + y * across.Length + x;
                    int b = a + across.Length;
                    mesh.Triangle(a, a + 1, b + 1);
                    mesh.Triangle(a, b + 1, b);
                }
            }
        }
    }

    public static ModelSlot Cylinder(string id, float radius, float height, ModelMaterial material,
        int radialSegments = 32, float? topRadius = null, Vector2? uvTileSizeMetres = null)
    {
        Positive(radius);
        Positive(height);
        float top = topRadius ?? radius;
        if (!float.IsFinite(top) || top < 0 || top > 1000)
        {
            throw new ArgumentException("AUR-PRIMITIVE-002: Top radius must be in 0..1000 metres.");
        }
        return Revolve(id, [new(radius, -height / 2), new(top, height / 2)], material, radialSegments,
            uvTileSizeMetres: uvTileSizeMetres);
    }

    /// <summary>Revolve an increasing-height (radius, height) profile around Y, with optional end caps.
    /// Profile corners retain hard normals. U spans the maximum circumference; V is height in metres.</summary>
    public static ModelSlot Revolve(string id, IReadOnlyList<Vector2> profile, ModelMaterial material,
        int radialSegments = 32, bool caps = true, Vector2? uvTileSizeMetres = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Count is < 2 or > 64 || radialSegments is < 3 or > 128)
        {
            throw new ArgumentException("AUR-PRIMITIVE-003: Revolve needs 2..64 profile points and 3..128 radial segments.");
        }
        float maximumRadius = 0;
        for (int index = 0; index < profile.Count; index++)
        {
            Vector2 point = profile[index];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < 0 || point.X > 1000
                || Math.Abs(point.Y) > 1000 || index > 0 && point.Y - profile[index - 1].Y < .00001f)
            {
                throw new ArgumentException("AUR-PRIMITIVE-004: Profile radii must be nonnegative and bounded; heights must increase by at least 0.00001 metres.");
            }
            maximumRadius = Math.Max(maximumRadius, point.X);
        }
        Positive(maximumRadius);
        var mesh = new Mesh(uvTileSizeMetres);
        for (int segment = 0; segment < profile.Count - 1; segment++)
        {
            Vector2 low = profile[segment];
            Vector2 high = profile[segment + 1];
            if (low.X == 0 && high.X == 0)
            {
                throw new ArgumentException("AUR-PRIMITIVE-004: A profile segment cannot lie entirely on the rotation axis.");
            }
            float slope = (high.X - low.X) / (high.Y - low.Y);
            int first = mesh.Count;
            foreach (Vector2 point in new[] { low, high })
            {
                for (int radial = 0; radial <= radialSegments; radial++)
                {
                    float angle = radial == radialSegments ? 0 : MathF.Tau * radial / radialSegments;
                    Vector3 outward = new(MathF.Cos(angle), 0, MathF.Sin(angle));
                    Vector3 normal = Vector3.Normalize(outward - Vector3.UnitY * slope);
                    Vector3 tangent = new(-MathF.Sin(angle), 0, MathF.Cos(angle));
                    mesh.Vertex(outward * point.X + Vector3.UnitY * point.Y, normal,
                        new(MathF.Tau * maximumRadius * radial / radialSegments, point.Y - profile[0].Y), new(tangent, -1));
                }
            }
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int a = first + radial;
                int b = a + radialSegments + 1;
                mesh.Triangle(a, b, b + 1);
                mesh.Triangle(a, b + 1, a + 1);
            }
        }
        if (caps)
        {
            Cap(profile[0], -1);
            Cap(profile[^1], 1);
        }
        return mesh.Build(id, material);

        void Cap(Vector2 point, float sign)
        {
            if (point.X == 0) { return; }
            Vector3 normal = Vector3.UnitY * sign;
            Vector4 tangent = new(1, 0, 0, -sign);
            int centre = mesh.Count;
            mesh.Vertex(new(0, point.Y, 0), normal, new(point.X), tangent);
            for (int radial = 0; radial <= radialSegments; radial++)
            {
                float angle = radial == radialSegments ? 0 : MathF.Tau * radial / radialSegments;
                Vector3 position = new(MathF.Cos(angle) * point.X, point.Y, MathF.Sin(angle) * point.X);
                mesh.Vertex(position, normal, new(position.X + point.X, position.Z + point.X), tangent);
            }
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int current = centre + 1 + radial;
                if (sign > 0) { mesh.Triangle(centre, current + 1, current); }
                else { mesh.Triangle(centre, current, current + 1); }
            }
        }
    }

    public static ModelSlot Sphere(string id, float radius, ModelMaterial material,
        int radialSegments = 32, int rings = 16, Vector2? uvTileSizeMetres = null)
    {
        Positive(radius);
        if (radialSegments is < 3 or > 128 || rings is < 2 or > 64)
        {
            throw new ArgumentException("AUR-PRIMITIVE-005: Sphere needs 3..128 radial segments and 2..64 rings.");
        }
        var mesh = new Mesh(uvTileSizeMetres);
        for (int ring = 0; ring <= rings; ring++)
        {
            float polar = MathF.PI * ring / rings;
            for (int radial = 0; radial <= radialSegments; radial++)
            {
                float angle = radial == radialSegments ? 0 : MathF.Tau * radial / radialSegments;
                Vector3 normal = new(MathF.Sin(polar) * MathF.Cos(angle), MathF.Cos(polar), MathF.Sin(polar) * MathF.Sin(angle));
                if (ring == 0) { normal = Vector3.UnitY; }
                if (ring == rings) { normal = -Vector3.UnitY; }
                mesh.Vertex(normal * radius, normal,
                    new(MathF.Tau * radius * radial / radialSegments, polar * radius),
                    new(-MathF.Sin(angle), 0, MathF.Cos(angle), 1));
            }
        }
        for (int ring = 0; ring < rings; ring++)
        {
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int a = ring * (radialSegments + 1) + radial;
                int b = a + radialSegments + 1;
                if (ring > 0) { mesh.Triangle(a, a + 1, b + 1); }
                if (ring < rings - 1) { mesh.Triangle(a, b + 1, b); }
            }
        }
        return mesh.Build(id, material);
    }

    private static float[] Coordinates(float extent, float bevel, int segments)
    {
        if (bevel == 0) { return [-extent, extent]; }
        var values = new List<float>();
        for (int index = 0; index <= segments; index++)
        {
            values.Add(-extent + bevel * index / segments);
        }
        for (int index = 0; index <= segments; index++)
        {
            values.Add(extent - bevel + bevel * index / segments);
        }
        return values.ToArray();
    }

    private static void Positive(float value)
    {
        if (!float.IsFinite(value) || value < .0001f || value > 1000)
        {
            throw new ArgumentException("AUR-PRIMITIVE-006: Dimensions must be finite and in 0.0001..1000 metres.");
        }
    }

    private sealed class Mesh
    {
        private readonly List<ModelVertex> vertices = [];
        private readonly List<int> indices = [];
        private readonly Vector2 tileSize;
        public int Count => vertices.Count;

        public Mesh(Vector2? uvTileSizeMetres)
        {
            tileSize = uvTileSizeMetres ?? Vector2.One;
            if (!float.IsFinite(tileSize.X) || !float.IsFinite(tileSize.Y)
                || tileSize.X < .01f || tileSize.Y < .01f || tileSize.X > 1000 || tileSize.Y > 1000)
            {
                throw new ArgumentException("AUR-PRIMITIVE-007: UV tile size must be in 0.01..1000 metres per axis.");
            }
        }

        public void Vertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 tangent)
        {
            vertices.Add(new(position, normal, uv / tileSize, tangent, Vector4.One));
        }

        public void Triangle(int a, int b, int c)
        {
            // Axis tips need one triangle, rather than a degenerate second half of the quad.
            Vector3 cross = Vector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
            if (cross.LengthSquared() <= 1e-20f) { return; }
            indices.AddRange([a, b, c]);
        }

        public ModelSlot Build(string id, ModelMaterial material)
        {
            ArgumentNullException.ThrowIfNull(material);
            material.Validate();
            var primitive = new ModelPrimitive(id, vertices.ToImmutableArray(), indices.ToImmutableArray(), material);
            string identity = ScenePlan.ComputePrimitiveModelIdentity(primitive);
            return new(new(id, identity, [new(id, primitive, Matrix4x4.Identity)]));
        }
    }
}
