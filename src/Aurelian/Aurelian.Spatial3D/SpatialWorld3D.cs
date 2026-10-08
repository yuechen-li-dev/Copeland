using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Spatial3D;

/// <summary>Immutable triangle world. Queries return facts; the caller owns agents and accepted state.</summary>
public sealed class SpatialWorld3D : ISpatialQueryWorld3D
{
    private readonly Triangle[] triangles;
    private readonly Node? root;
    private readonly Dictionary<string, Bounds> colliderBounds = new(StringComparer.Ordinal);
    public ImmutableArray<Collider3D> Colliders { get; }

    private readonly record struct Bounds(Vector3 Min, Vector3 Max)
    {
        public bool Intersects(Bounds other) => Min.X <= other.Max.X && Max.X >= other.Min.X
            && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
        public Bounds Expanded(float radius) => new(Min - new Vector3(radius), Max + new Vector3(radius));
        public static Bounds Segment(Vector3 a, Vector3 b) => new(Vector3.Min(a, b), Vector3.Max(a, b));
        public static Bounds Union(Bounds a, Bounds b) => new(Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max));
    }

    private sealed record Triangle(Collider3D Collider, int Index, Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, Bounds Bounds);
    private sealed record Node(Bounds Bounds, Node? Left, Node? Right, int[] Leaves);

    public SpatialWorld3D(IEnumerable<Collider3D> colliders)
    {
        Colliders = colliders.OrderBy(collider => collider.Id, StringComparer.Ordinal).ToImmutableArray();
        if (Colliders.Any(collider => string.IsNullOrWhiteSpace(collider.Id) || collider.Layer == 0)
            || Colliders.Select(collider => collider.Id).Distinct(StringComparer.Ordinal).Count() != Colliders.Length)
        {
            throw new ArgumentException("Colliders need unique nonempty identities and nonzero layers.");
        }
        var built = new List<Triangle>();
        foreach (Collider3D collider in Colliders)
        {
            Matrix4x4 matrix = collider.Transform;
            if (!Matrix4x4.Invert(matrix, out _) || matrix.GetDeterminant() <= 0
                || matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 != 1)
            {
                throw new ArgumentException("Collider transforms must be finite orientation-preserving affine frames.");
            }
            Vector3[] points = collider.Mesh.Positions.Select(point => Vector3.Transform(point, matrix)).ToArray();
            foreach (Vector3 point in points) SpatialMath3D.RequireFinite(point);
            for (int index = 0; index < collider.Mesh.Indices.Length; index += 3)
            {
                Vector3 a = points[collider.Mesh.Indices[index]];
                Vector3 b = points[collider.Mesh.Indices[index + 1]];
                Vector3 c = points[collider.Mesh.Indices[index + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-16f) throw new ArgumentException("Transformed collision triangles are degenerate.");
                built.Add(new(collider, index / 3, a, b, c, Vector3.Normalize(cross),
                    Bounds.Union(Bounds.Segment(a, b), Bounds.Segment(b, c))));
            }
        }
        triangles = built.ToArray();
        foreach (Collider3D collider in Colliders)
        {
            colliderBounds.Add(collider.Id, triangles.Where(triangle => triangle.Collider.Id == collider.Id)
                .Select(triangle => triangle.Bounds).Aggregate(Bounds.Union));
        }
        root = triangles.Length == 0 ? null : Build(Enumerable.Range(0, triangles.Length).ToArray());
    }

    public SpatialHit3D? Raycast(Ray3D ray, QueryFilter3D? filter = null)
    {
        ray.Validate();
        SpatialHit3D? best = null;
        foreach (Triangle triangle in Candidates(Bounds.Segment(ray.Origin, ray.Origin + ray.Direction * ray.MaximumDistance), filter))
        {
            float? distance = TriangleQueries3D.Ray(ray.Origin, ray.Direction, triangle.A, triangle.B, triangle.C);
            if (distance is not { } value || value > ray.MaximumDistance) continue;
            Vector3 normal = Vector3.Dot(triangle.Normal, ray.Direction) > 0 ? -triangle.Normal : triangle.Normal;
            var hit = Hit(triangle, value, value / ray.MaximumDistance, ray.Origin + ray.Direction * value, normal);
            if (Better(hit, best)) best = hit;
        }
        return best;
    }

    public ImmutableArray<SpatialHit3D> Overlap(Capsule3D shape, QueryFilter3D? filter = null)
    {
        shape.Validate();
        var hits = new Dictionary<string, SpatialHit3D>(StringComparer.Ordinal);
        foreach (Triangle triangle in Candidates(Bounds.Segment(shape.A, shape.B).Expanded(shape.Radius), filter))
        {
            var closest = TriangleQueries3D.ClosestSegment(shape.A, shape.B, triangle.A, triangle.B, triangle.C);
            Vector3 delta = closest.OnSegment - closest.OnTriangle;
            if (delta.Length() >= shape.Radius - SpatialMath3D.Epsilon) continue;
            Vector3 normal = delta.LengthSquared() > 1e-12f ? Vector3.Normalize(delta) : triangle.Normal;
            hits.TryAdd(triangle.Collider.Id, Hit(triangle, 0, 0, closest.OnTriangle, normal, SpatialQueryStatus3D.InitiallyOverlapping));
        }
        // A capsule fully inside a closed solid may touch no face. Report it explicitly;
        // recovery is caller policy rather than an arbitrary engine teleport.
        foreach (Collider3D collider in Colliders.Where(collider => collider.Mesh.Closed
            && (filter ?? QueryFilter3D.All).Matches(collider.Layer, collider.Mask) && !hits.ContainsKey(collider.Id)))
        {
            if (!colliderBounds[collider.Id].Intersects(new(shape.A, shape.A)) || !Inside(collider.Id, shape.A)) continue;
            Triangle triangle = triangles.First(triangle => triangle.Collider.Id == collider.Id);
            hits.Add(collider.Id, Hit(triangle, 0, 0, shape.A, triangle.Normal, SpatialQueryStatus3D.InitiallyOverlapping));
        }
        return hits.Values.OrderBy(hit => hit.ColliderId, StringComparer.Ordinal).ToImmutableArray();
    }

    public SpatialHit3D? Sweep(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null)
    {
        shape.Validate();
        SpatialMath3D.RequireFinite(displacement);
        var overlaps = Overlap(shape, filter);
        if (!overlaps.IsEmpty) return overlaps[0];
        float length = displacement.Length();
        if (length < 1e-8f) return null;
        Bounds bounds = Bounds.Union(Bounds.Segment(shape.A, shape.B),
            Bounds.Segment(shape.A + displacement, shape.B + displacement)).Expanded(shape.Radius + SpatialMath3D.Epsilon);
        SpatialHit3D? best = null;
        foreach (Triangle triangle in Candidates(bounds, filter))
        {
            float time = 0;
            for (int iteration = 0; iteration < 64; iteration++)
            {
                var closest = TriangleQueries3D.ClosestSegment(shape.A + displacement * time,
                    shape.B + displacement * time, triangle.A, triangle.B, triangle.C);
                Vector3 delta = closest.OnSegment - closest.OnTriangle;
                float distance = delta.Length();
                Vector3 normal = distance > 1e-8f ? delta / distance : triangle.Normal;
                float closing = -Vector3.Dot(displacement, normal);
                if (closing <= 1e-8f) break;
                float gap = distance - shape.Radius;
                if (gap <= SpatialMath3D.Epsilon || iteration == 63)
                {
                    var status = iteration == 63 ? SpatialQueryStatus3D.IterationLimit : SpatialQueryStatus3D.Contact;
                    var hit = Hit(triangle, time * length, time, closest.OnTriangle, normal, status);
                    if (Better(hit, best)) best = hit;
                    break;
                }
                time += gap / closing;
                if (time > 1 || (best is { } existing && time > existing.TimeOfImpact + SpatialMath3D.Epsilon)) break;
            }
        }
        return best;
    }

    public MoveResult3D SweepAndSlide(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null,
        int maximumIterations = 6, float minimumGroundNormalY = 0)
    {
        if (maximumIterations is < 1 or > 16 || !float.IsFinite(minimumGroundNormalY)
            || minimumGroundNormalY < 0 || minimumGroundNormalY > 1) throw new ArgumentOutOfRangeException(nameof(maximumIterations));
        shape.Validate();
        SpatialMath3D.RequireFinite(displacement);
        Vector3 accepted = Vector3.Zero;
        Vector3 remaining = displacement;
        var contacts = ImmutableArray.CreateBuilder<SpatialHit3D>();
        int iterations = 0;
        for (; iterations < maximumIterations && remaining.LengthSquared() > 1e-12f; iterations++)
        {
            var hit = Sweep(shape.Translated(accepted), remaining, filter);
            if (hit is null)
            {
                accepted += remaining;
                break;
            }
            contacts.Add(hit.Value);
            if (hit.Value.Status == SpatialQueryStatus3D.InitiallyOverlapping)
            {
                return new(displacement, accepted, contacts.ToImmutable(), true, iterations + 1);
            }
            float travel = MathF.Max(0, hit.Value.TimeOfImpact - 0.00001f / remaining.Length());
            accepted += remaining * travel;
            remaining *= 1 - travel;
            Vector3 normal = hit.Value.Normal;
            if (normal.Y > 0 && normal.Y < minimumGroundNormalY)
            {
                normal = Vector3.Normalize(new Vector3(normal.X, 0, normal.Z));
            }
            float into = Vector3.Dot(remaining, normal);
            if (into < 0) remaining -= normal * into;
            if (hit.Value.Status == SpatialQueryStatus3D.IterationLimit) break;
        }
        return new(displacement, accepted, contacts.ToImmutable(), false, iterations + 1);
    }

    public static float RaySphere(Vector3 origin, Vector3 unitDirection, Vector3 center, float radius)
    {
        new Ray3D(origin, unitDirection, 1).Validate();
        Capsule3D.Sphere(center, radius).Validate();
        Vector3 offset = origin - center;
        float b = Vector3.Dot(offset, unitDirection);
        float c = offset.LengthSquared() - radius * radius;
        if (c <= 0) return 0;
        float discriminant = b * b - c;
        if (discriminant < 0) return float.PositiveInfinity;
        float distance = -b - MathF.Sqrt(discriminant);
        return distance >= 0 ? distance : float.PositiveInfinity;
    }

    private bool Inside(string id, Vector3 point)
    {
        Vector3 direction = Vector3.Normalize(new Vector3(1, 0.371f, 0.529f));
        var hits = new List<float>();
        foreach (Triangle triangle in triangles.Where(triangle => triangle.Collider.Id == id))
        {
            var distance = TriangleQueries3D.Ray(point, direction, triangle.A, triangle.B, triangle.C);
            if (distance is > SpatialMath3D.Epsilon) hits.Add(distance.Value);
        }
        hits.Sort();
        int crossings = 0;
        float previous = float.NegativeInfinity;
        foreach (float hit in hits)
        {
            if (hit - previous > SpatialMath3D.Epsilon) crossings++;
            previous = hit;
        }
        return crossings % 2 != 0;
    }

    private static SpatialHit3D Hit(Triangle triangle, float distance, float time, Vector3 point, Vector3 normal,
        SpatialQueryStatus3D status = SpatialQueryStatus3D.Contact) => new(triangle.Collider.Id, triangle.Index,
            distance, time, point, normal, status, triangle.Collider.SemanticOwnerId,
            triangle.Collider.Mesh.SourceIdentity, triangle.Collider.Mesh.TriangleFaces.IsEmpty ? null : triangle.Collider.Mesh.TriangleFaces[triangle.Index]);

    private static bool Better(SpatialHit3D candidate, SpatialHit3D? current)
    {
        if (current is null) return true;
        float delta = candidate.Distance - current.Value.Distance;
        if (MathF.Abs(delta) > SpatialMath3D.Epsilon) return delta < 0;
        int identity = StringComparer.Ordinal.Compare(candidate.ColliderId, current.Value.ColliderId);
        return identity < 0 || (identity == 0 && candidate.TriangleIndex < current.Value.TriangleIndex);
    }

    private IEnumerable<Triangle> Candidates(Bounds bounds, QueryFilter3D? filter)
    {
        if (root is null) yield break;
        QueryFilter3D actual = filter ?? QueryFilter3D.All;
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            if (!node.Bounds.Intersects(bounds)) continue;
            if (node.Left is not null)
            {
                stack.Push(node.Right!);
                stack.Push(node.Left);
                continue;
            }
            foreach (int index in node.Leaves)
            {
                Triangle triangle = triangles[index];
                if (triangle.Bounds.Intersects(bounds) && actual.Matches(triangle.Collider.Layer, triangle.Collider.Mask)) yield return triangle;
            }
        }
    }

    private Node Build(int[] indices)
    {
        Bounds bounds = indices.Select(index => triangles[index].Bounds).Aggregate(Bounds.Union);
        if (indices.Length <= 12) return new(bounds, null, null, indices);
        Vector3 size = bounds.Max - bounds.Min;
        int axis = 2;
        if (size.X >= size.Y && size.X >= size.Z)
        {
            axis = 0;
        }
        else if (size.Y >= size.Z)
        {
            axis = 1;
        }
        Array.Sort(indices, (a, b) =>
        {
            float x = (triangles[a].A[axis] + triangles[a].B[axis] + triangles[a].C[axis]) / 3;
            float y = (triangles[b].A[axis] + triangles[b].B[axis] + triangles[b].C[axis]) / 3;
            int order = x.CompareTo(y);
            return order == 0 ? a.CompareTo(b) : order;
        });
        int middle = indices.Length / 2;
        return new(bounds, Build(indices[..middle]), Build(indices[middle..]), []);
    }
}
