using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>Immutable local-space geometry. Identity and validation belong to the asset, not a frame.</summary>
public sealed class NativeMesh3D
{
    public string Identity { get; }
    public ImmutableArray<NativeModel3DVertex> Vertices { get; }
    public ImmutableArray<uint> Indices { get; }
    public ImmutableArray<uint> PackedIndices { get; }
    public ImmutableArray<NativeLodRange3D> LodRanges { get; }
    public AnalyticPrimitiveKind3D PrimitiveKind { get; }

    public NativeMesh3D(string identity, IEnumerable<NativeModel3DVertex> vertices, IEnumerable<uint> indices,
        IEnumerable<ModelLod>? lods = null)
        : this(identity, vertices, indices, lods, AnalyticPrimitiveKind3D.Triangles)
    {
    }

    internal NativeMesh3D(string identity, IEnumerable<NativeModel3DVertex> vertices, IEnumerable<uint> indices,
        IEnumerable<ModelLod>? lods, AnalyticPrimitiveKind3D primitiveKind)
    {
        PrimitiveKind = primitiveKind;
        Identity = identity;
        Vertices = vertices.ToImmutableArray();
        Indices = indices.ToImmutableArray();
        if (string.IsNullOrWhiteSpace(identity) || Vertices.IsEmpty || Indices.IsEmpty
            || Indices.Length % 3 != 0 || Indices.Any(index => index >= Vertices.Length))
        {
            throw new InvalidDataException("AUR-MESH-001: Meshes require an identity and in-range complete indexed triangles.");
        }
        foreach (NativeModel3DVertex vertex in Vertices)
        {
            if (!Finite(vertex.Position) || !Finite(vertex.Normal) || !Finite(vertex.Color)
                || !Finite(vertex.Tangent) || !float.IsFinite(vertex.Uv.X) || !float.IsFinite(vertex.Uv.Y))
            {
                throw new InvalidDataException("AUR-MESH-002: Mesh vertices must be finite.");
            }
        }
        ModelLod[] levels = lods?.ToArray() ?? [];
        ModelLod.ValidateChain(levels.ToImmutableArray(), Vertices.Length, Indices.Length);
        var packed = Indices.ToBuilder();
        var ranges = ImmutableArray.CreateBuilder<NativeLodRange3D>();
        ranges.Add(new(0, 0, (uint)Indices.Length));
        foreach (var level in levels)
        {
            ranges.Add(new(level.Error, (uint)packed.Count, (uint)level.Indices.Length));
            packed.AddRange(level.Indices.Select(index => (uint)index));
        }
        PackedIndices = packed.ToImmutable();
        LodRanges = ranges.ToImmutable();
    }

    public static NativeMesh3D FromPrimitive(ModelPrimitive primitive) => new(primitive.Id,
        primitive.Vertices.Select(vertex => new NativeModel3DVertex(vertex.Position, vertex.Normal,
            vertex.Color, vertex.Uv, vertex.Tangent)), primitive.Indices.Select(index => (uint)index), primitive.Lods);

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

public readonly record struct NativeLodRange3D(float Error, uint FirstIndex, uint IndexCount);

public sealed record SceneInstance3D(string Identity, NativeMesh3D Mesh, ModelMaterial Material, Matrix4x4 Transform)
{
    public Matrix4x4? PreviousTransform { get; init; }
    public uint RayLayer { get; init; } = 1;
    public uint RayMask { get; init; } = uint.MaxValue;

    /// <summary>Compatibility for forward-only materials and the existing planar reflection renderer.</summary>
    public NativeModel3DBatch Expand()
    {
        if (Mesh.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles)
            throw new NotSupportedException("AUR-HYBRID-001: Analytic geometry requires the resident hybrid visibility path.");
        if (!Matrix4x4.Invert(Transform, out var inverse))
        {
            throw new InvalidDataException("AUR-MESH-011: Instance transforms must be invertible.");
        }
        Matrix4x4 normals = Matrix4x4.Transpose(inverse);
        var vertices = new NativeModel3DVertex[Mesh.Indices.Length];
        for (int index = 0; index < vertices.Length; index++)
        {
            var vertex = Mesh.Vertices[(int)Mesh.Indices[index]];
            Vector3 normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normals));
            Vector3 tangent = Vector3.TransformNormal(new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z), Transform);
            tangent = Vector3.Normalize(tangent - normal * Vector3.Dot(normal, tangent));
            vertices[index] = vertex with
            {
                Position = Vector3.Transform(vertex.Position, Transform),
                Normal = normal,
                Tangent = new(tangent, vertex.Tangent.W),
            };
        }
        return new(vertices, Material) { TemporalIdentity = Identity };
    }
}
