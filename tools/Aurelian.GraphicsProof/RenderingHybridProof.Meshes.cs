using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Assets.Models;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHybridProof
{
    private static NativeMesh3D Mesh(AnalyticPrimitiveKind3D kind, int slices = 64, bool lod = false)
    {
        var vertices = new List<NativeModel3DVertex>();
        var indices = new List<uint>();
        if (kind == AnalyticPrimitiveKind3D.UnitBox)
        {
            foreach (Vector3 normal in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
            {
                Vector3 tangent = Math.Abs(normal.X) > .5f ? -Vector3.UnitZ : Vector3.UnitX;
                float handedness = normal.X + normal.Y + normal.Z;
                Vector3 bitangent = Vector3.Cross(normal, tangent) * handedness;
                uint first = (uint)vertices.Count;
                foreach (Vector2 corner in new[] { new Vector2(-1, -1), new(1, -1), new(1, 1), new(-1, 1) })
                {
                    Vector3 position = normal + tangent * corner.X + bitangent * corner.Y;
                    vertices.Add(new(position, normal, Vector4.One, (corner + Vector2.One) * .5f, new(tangent, handedness)));
                }
                if (handedness > 0) indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
                else indices.AddRange([first, first + 2, first + 1, first, first + 3, first + 2]);
            }
        }
        else
        {
            var profiles = new List<(float Radius, float Height, float NormalY, float NormalRadius)>();
            int half = slices / 4;
            if (kind == AnalyticPrimitiveKind3D.UnitSphere)
            {
                for (int ring = 0; ring <= slices / 2; ring++)
                {
                    float angle = MathF.PI * ring / (slices / 2);
                    profiles.Add((MathF.Sin(angle), MathF.Cos(angle), MathF.Cos(angle), MathF.Sin(angle)));
                }
            }
            else
            {
                foreach (float center in new[] { .5f, -.5f })
                {
                    for (int ring = 0; ring <= half; ring++)
                    {
                        float angle = MathF.PI * .5f * ring / half;
                        if (center < 0) angle += MathF.PI * .5f;
                        profiles.Add((.5f * MathF.Sin(angle), center + .5f * MathF.Cos(angle), MathF.Cos(angle), MathF.Sin(angle)));
                    }
                }
            }
            foreach (var profile in profiles)
            {
                for (int slice = 0; slice <= slices; slice++)
                {
                    float angle = MathF.Tau * slice / slices;
                    Vector3 normal = Vector3.Normalize(new(profile.NormalRadius * MathF.Cos(angle), profile.NormalY,
                        profile.NormalRadius * MathF.Sin(angle)));
                    Vector3 position = new(profile.Radius * MathF.Cos(angle), profile.Height, profile.Radius * MathF.Sin(angle));
                    Vector3 tangent = Vector3.Cross(Vector3.UnitY, normal);
                    float sign = 1;
                    if (normal.Z < 0) { tangent = -tangent; sign = -1; }
                    if (tangent.LengthSquared() < 1e-12f) tangent = Vector3.UnitX;
                    vertices.Add(new(position, normal, Vector4.One, new(position.X * .5f + .5f, position.Y * .5f + .5f),
                        new(Vector3.Normalize(tangent), sign)));
                }
            }
            for (int ring = 0; ring < profiles.Count - 1; ring++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    uint first = (uint)(ring * (slices + 1) + slice);
                    uint next = first + (uint)slices + 1;
                    indices.AddRange([first, first + 1, next, first + 1, next + 1, next]);
                }
            }
        }
        if (!lod) return new("mesh-" + kind + "-" + slices, vertices, indices);
        var primitive = new ModelPrimitive("lod-" + kind + "-" + slices, vertices.Select(vertex =>
            new ModelVertex(vertex.Position, vertex.Normal, vertex.Uv, vertex.Tangent, vertex.Color)).ToImmutableArray(),
            indices.Select(index => (int)index).ToImmutableArray(), new("source") { Metallic = 0 });
        return NativeMesh3D.FromPrimitive(ModelLodCompiler.Compile(primitive));
    }
}
