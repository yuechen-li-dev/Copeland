using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Spatial3D;

public readonly record struct QueryFilter3D(uint IncludedLayers = uint.MaxValue, uint QueryLayer = uint.MaxValue)
{
    public QueryFilter3D() : this(uint.MaxValue, uint.MaxValue)
    {
    }

    public static QueryFilter3D All => new(uint.MaxValue, uint.MaxValue);
    public bool Matches(uint layer, uint mask) => (IncludedLayers & layer) != 0 && (QueryLayer & mask) != 0;
}

public readonly record struct Ray3D(Vector3 Origin, Vector3 Direction, float MaximumDistance)
{
    public void Validate()
    {
        SpatialMath3D.RequireFinite(Origin);
        SpatialMath3D.RequireFinite(Direction);
        if (MathF.Abs(Direction.LengthSquared() - 1) > 0.0001f
            || !float.IsFinite(MaximumDistance) || MaximumDistance <= 0)
        {
            throw new ArgumentException("A ray needs a unit direction and positive finite maximum distance.");
        }
    }
}

/// <summary>World-space segment endpoints are the centres of the two spherical caps.</summary>
public readonly record struct Capsule3D(Vector3 A, Vector3 B, float Radius)
{
    public static Capsule3D AtFeet(Vector3 feet, float radius = 0.3f, float height = 1.8f)
    {
        if (!float.IsFinite(height) || height < 2 * radius)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
        return new(feet + Vector3.UnitY * radius, feet + Vector3.UnitY * (height - radius), radius);
    }

    public static Capsule3D Sphere(Vector3 center, float radius) => new(center, center, radius);
    public Capsule3D Translated(Vector3 offset) => new(A + offset, B + offset, Radius);
    public void Validate()
    {
        SpatialMath3D.RequireFinite(A);
        SpatialMath3D.RequireFinite(B);
        if (!float.IsFinite(Radius) || Radius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Radius));
        }
    }
}

public sealed class CollisionMesh3D
{
    public CollisionMesh3D(IEnumerable<Vector3> positions, IEnumerable<int> indices, bool closed = false,
        string? sourceIdentity = null, IEnumerable<string?>? triangleFaces = null)
    {
        Positions = positions.ToImmutableArray();
        Indices = indices.ToImmutableArray();
        TriangleFaces = triangleFaces?.ToImmutableArray() ?? [];
        Closed = closed;
        SourceIdentity = sourceIdentity;
        if (Positions.IsDefaultOrEmpty || Indices.IsDefaultOrEmpty || Indices.Length % 3 != 0
            || Indices.Any(index => index < 0 || index >= Positions.Length)
            || (!TriangleFaces.IsEmpty && TriangleFaces.Length != Indices.Length / 3))
        {
            throw new ArgumentException("Collision meshes need valid indexed triangles and matching optional face identities.");
        }
        foreach (Vector3 position in Positions)
        {
            SpatialMath3D.RequireFinite(position);
        }
        for (int index = 0; index < Indices.Length; index += 3)
        {
            if (Vector3.Cross(Positions[Indices[index + 1]] - Positions[Indices[index]],
                Positions[Indices[index + 2]] - Positions[Indices[index]]).LengthSquared() < 1e-16f)
            {
                throw new ArgumentException("Collision meshes cannot contain degenerate triangles.");
            }
        }
        if (closed) ValidateClosed();
    }

    public ImmutableArray<Vector3> Positions { get; }
    public ImmutableArray<int> Indices { get; }
    public ImmutableArray<string?> TriangleFaces { get; }
    public bool Closed { get; }
    public string? SourceIdentity { get; }

    private void ValidateClosed()
    {
        // Weld exact face-duplicated positions for validation, retaining display topology.
        var vertices = new Dictionary<Vector3, int>();
        int[] canonical = Positions.Select(point =>
        {
            if (!vertices.TryGetValue(point, out int id))
            {
                id = vertices.Count;
                vertices.Add(point, id);
            }
            return id;
        }).ToArray();
        var edges = new Dictionary<(int A, int B), (int Count, int Orientation)>();
        for (int index = 0; index < Indices.Length; index += 3)
        {
            for (int side = 0; side < 3; side++)
            {
                int a = canonical[Indices[index + side]];
                int b = canonical[Indices[index + (side + 1) % 3]];
                var key = (Math.Min(a, b), Math.Max(a, b));
                var previous = edges.GetValueOrDefault(key);
                edges[key] = (previous.Count + 1, previous.Orientation + (a < b ? 1 : -1));
            }
        }
        if (edges.Values.Any(edge => edge.Count != 2 || edge.Orientation != 0))
        {
            throw new ArgumentException("Closed collision geometry must have consistently oriented manifold edges.");
        }
    }

    public static CollisionMesh3D Box(Vector3 halfSize)
    {
        SpatialMath3D.RequireFinite(halfSize);
        if (halfSize.X <= 0 || halfSize.Y <= 0 || halfSize.Z <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(halfSize));
        }
        Vector3[] points =
        [
            new(-halfSize.X, -halfSize.Y, -halfSize.Z), new(halfSize.X, -halfSize.Y, -halfSize.Z),
            new(halfSize.X, halfSize.Y, -halfSize.Z), new(-halfSize.X, halfSize.Y, -halfSize.Z),
            new(-halfSize.X, -halfSize.Y, halfSize.Z), new(halfSize.X, -halfSize.Y, halfSize.Z),
            new(halfSize.X, halfSize.Y, halfSize.Z), new(-halfSize.X, halfSize.Y, halfSize.Z),
        ];
        return new(points, [0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7, 0, 4, 7, 0, 7, 3,
            1, 2, 6, 1, 6, 5, 3, 7, 6, 3, 6, 2, 0, 1, 5, 0, 5, 4], closed: true);
    }
}

public sealed record Collider3D(string Id, CollisionMesh3D Mesh, Matrix4x4 Transform,
    uint Layer = 1, uint Mask = uint.MaxValue, string? SemanticOwnerId = null);

public enum SpatialQueryStatus3D { Contact, InitiallyOverlapping, IterationLimit }

public readonly record struct SpatialHit3D(string ColliderId, int TriangleIndex, float Distance,
    float TimeOfImpact, Vector3 Point, Vector3 Normal, SpatialQueryStatus3D Status,
    string? SemanticOwnerId = null, string? SourceIdentity = null, string? FaceId = null);

public sealed record MoveResult3D(Vector3 RequestedDisplacement, Vector3 AcceptedDisplacement,
    ImmutableArray<SpatialHit3D> Contacts, bool InitiallyOverlapping, int Iterations);

public interface IRayQueryWorld3D
{
    SpatialHit3D? Raycast(Ray3D ray, QueryFilter3D? filter = null);
}

/// <summary>Movement consumes facts without depending on mesh versus BRep representation.</summary>
public interface ISpatialQueryWorld3D : IRayQueryWorld3D
{
    ImmutableArray<SpatialHit3D> Overlap(Capsule3D shape, QueryFilter3D? filter = null);
    SpatialHit3D? Sweep(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null);
    MoveResult3D SweepAndSlide(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null,
        int maximumIterations = 6, float minimumGroundNormalY = 0);
}

internal static class SpatialMath3D
{
    internal const float Epsilon = 0.00001f;
    internal static void RequireFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentException("Spatial coordinates must be finite.");
        }
    }
}
