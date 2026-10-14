using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Spatial3D;

/// <summary>
/// A collider the game moves explicitly (lifts, shuttles, turntables). Geometry is authored in local space and
/// never rebuilt; only its rigid transform changes.
/// </summary>
public sealed record KinematicBody3D(string Id, CollisionMesh3D Mesh, uint Layer = 1, uint Mask = uint.MaxValue,
    string? SemanticOwnerId = null);

/// <summary>What a kinematic world imposes on a character before its own movement this step.</summary>
/// <param name="Displacement">Carry from the body it stands on plus pushes from bodies that moved into it.</param>
/// <param name="Yaw">Rotation (radians about +Y) of the supporting body since the previous step.</param>
/// <param name="SupportId">The kinematic body carrying the character, if any.</param>
/// <param name="PushedBy">Kinematic bodies that moved into the character this step, in identity order.</param>
public readonly record struct KinematicCarry3D(Vector3 Displacement, float Yaw, string? SupportId, ImmutableArray<string> PushedBy)
{
    public static KinematicCarry3D None => new(Vector3.Zero, 0, null, []);
}

/// <summary>
/// An immutable static world plus explicitly moved kinematic bodies. Static geometry keeps the rebuild-on-change
/// contract of <see cref="SpatialWorld3D"/>; each body's triangles are built once in local space and queried through
/// its current rigid transform. The world records each body's previous transform so callers can derive carry,
/// push and platform velocity without guessing. Nothing here moves characters: it only reports facts.
/// </summary>
public sealed class KinematicSpatialWorld3D : ISpatialQueryWorld3D
{
    private readonly SortedDictionary<string, BodyState> bodies = new(StringComparer.Ordinal);

    private sealed class BodyState(KinematicBody3D body, SpatialWorld3D local, Matrix4x4 transform)
    {
        public KinematicBody3D Body { get; } = body;
        public SpatialWorld3D Local { get; } = local;
        public Matrix4x4 Current { get; set; } = transform;
        public Matrix4x4 Previous { get; set; } = transform;
        public Matrix4x4 Inverse { get; set; } = Invert(transform);
    }

    public KinematicSpatialWorld3D(ISpatialQueryWorld3D statics, IEnumerable<KinematicBody3D> kinematic,
        IReadOnlyDictionary<string, Matrix4x4>? initialTransforms = null)
    {
        Statics = statics ?? throw new ArgumentNullException(nameof(statics));
        ArgumentNullException.ThrowIfNull(kinematic);
        var staticIds = statics is SpatialWorld3D world
            ? world.Colliders.Select(collider => collider.Id).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        foreach (KinematicBody3D body in kinematic)
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Id) || body.Layer == 0 || bodies.ContainsKey(body.Id)
                || staticIds.Contains(body.Id))
            {
                throw new ArgumentException("Kinematic bodies need unique nonempty identities, distinct from static colliders, and nonzero layers.");
            }
            Matrix4x4 transform = initialTransforms is not null && initialTransforms.TryGetValue(body.Id, out Matrix4x4 given)
                ? given : Matrix4x4.Identity;
            ValidateRigid(transform);
            var local = new SpatialWorld3D([new Collider3D(body.Id, body.Mesh, Matrix4x4.Identity, body.Layer, body.Mask,
                body.SemanticOwnerId)]);
            bodies.Add(body.Id, new BodyState(body, local, transform));
        }
    }

    public ISpatialQueryWorld3D Statics { get; }
    public IEnumerable<string> BodyIds => bodies.Keys;
    public bool IsKinematic(string id) => id is not null && bodies.ContainsKey(id);
    public Matrix4x4 Transform(string id) => State(id).Current;
    public Matrix4x4 PreviousTransform(string id) => State(id).Previous;

    /// <summary>Start a simulation step: every body's current transform becomes its previous transform.</summary>
    public void BeginStep()
    {
        foreach (BodyState state in bodies.Values)
        {
            state.Previous = state.Current;
        }
    }

    /// <summary>Move a body for this step. Transforms must be rigid (rotation and translation only).</summary>
    public void SetTransform(string id, Matrix4x4 transform)
    {
        ValidateRigid(transform);
        BodyState state = State(id);
        state.Current = transform;
        state.Inverse = Invert(transform);
    }

    /// <summary>Restore both transforms exactly, for snapshots.</summary>
    public void Restore(string id, Matrix4x4 previous, Matrix4x4 current)
    {
        ValidateRigid(previous);
        ValidateRigid(current);
        BodyState state = State(id);
        state.Previous = previous;
        state.Current = current;
        state.Inverse = Invert(current);
    }

    /// <summary>Where a point attached to the body this step has moved: current(previous⁻¹(point)) − point.</summary>
    public Vector3 PointDisplacement(string id, Vector3 point)
    {
        SpatialMath3D.RequireFinite(point);
        BodyState state = State(id);
        Vector3 local = Vector3.Transform(point, Invert(state.Previous));
        return Vector3.Transform(local, state.Current) - point;
    }

    /// <summary>The body's rotation about +Y since the previous step, in radians.</summary>
    public float YawDelta(string id)
    {
        BodyState state = State(id);
        Vector3 before = Vector3.TransformNormal(Vector3.UnitX, state.Previous);
        Vector3 after = Vector3.TransformNormal(Vector3.UnitX, state.Current);
        float a = MathF.Atan2(-before.Z, before.X);
        float b = MathF.Atan2(-after.Z, after.X);
        return MathF.IEEERemainder(b - a, MathF.Tau);
    }

    /// <summary>
    /// The carry a character receives this step: it rides <paramref name="supportId"/> (the ground collider reported by its
    /// previous motor step) and is pushed by any other body that moved into its capsule. Sweep the result against
    /// <see cref="Statics"/> (the motor's <c>carryWorld</c>) so bodies cannot push characters through walls.
    /// </summary>
    public KinematicCarry3D Carry(Capsule3D character, string? supportId, QueryFilter3D? filter = null)
    {
        character.Validate();
        Vector3 feet = character.A - Vector3.UnitY * character.Radius;
        Vector3 displacement = Vector3.Zero;
        float yaw = 0;
        string? support = supportId is not null && bodies.ContainsKey(supportId) ? supportId : null;
        if (support is not null)
        {
            displacement = PointDisplacement(support, feet);
            yaw = YawDelta(support);
        }
        var pushed = ImmutableArray.CreateBuilder<string>();
        QueryFilter3D actual = filter ?? QueryFilter3D.All;
        Capsule3D carried = character.Translated(displacement);
        foreach (BodyState state in bodies.Values)
        {
            if (state.Body.Id == support || !actual.Matches(state.Body.Layer, state.Body.Mask) || state.Current == state.Previous)
            {
                continue;
            }
            if (OverlapBody(state, carried, filter).IsEmpty)
            {
                continue;
            }
            // Push by the body's own motion at the capsule centre: a shuttle moving into a standing character moves it along.
            Vector3 centre = (carried.A + carried.B) / 2;
            Vector3 push = PointDisplacement(state.Body.Id, centre);
            displacement += push;
            carried = carried.Translated(push);
            pushed.Add(state.Body.Id);
        }
        return new KinematicCarry3D(displacement, yaw, support, pushed.ToImmutable());
    }

    /// <summary>Linear velocity of a point riding the body over the last step, for momentum when leaving it.</summary>
    public Vector3 PointVelocity(string id, Vector3 point, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        return PointDisplacement(id, point) / seconds;
    }

    public SpatialHit3D? Raycast(Ray3D ray, QueryFilter3D? filter = null)
    {
        ray.Validate();
        SpatialHit3D? best = Statics.Raycast(ray, filter);
        foreach (BodyState state in bodies.Values)
        {
            var local = new Ray3D(Vector3.Transform(ray.Origin, state.Inverse),
                Vector3.Normalize(Vector3.TransformNormal(ray.Direction, state.Inverse)), ray.MaximumDistance);
            SpatialHit3D? hit = state.Local.Raycast(local, filter);
            if (hit is { } value)
            {
                best = Better(ToWorld(value, state), best);
            }
        }
        return best;
    }

    public ImmutableArray<SpatialHit3D> Overlap(Capsule3D shape, QueryFilter3D? filter = null)
    {
        shape.Validate();
        var hits = new List<SpatialHit3D>(Statics.Overlap(shape, filter));
        foreach (BodyState state in bodies.Values)
        {
            hits.AddRange(OverlapBody(state, shape, filter));
        }
        return hits.OrderBy(hit => hit.ColliderId, StringComparer.Ordinal).ToImmutableArray();
    }

    public SpatialHit3D? Sweep(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null)
    {
        shape.Validate();
        SpatialMath3D.RequireFinite(displacement);
        SpatialHit3D? best = Statics.Sweep(shape, displacement, filter);
        foreach (BodyState state in bodies.Values)
        {
            SpatialHit3D? hit = state.Local.Sweep(ToLocal(shape, state), Vector3.TransformNormal(displacement, state.Inverse), filter);
            if (hit is { } value)
            {
                best = Better(ToWorld(value, state), best);
            }
        }
        return best;
    }

    public MoveResult3D SweepAndSlide(Capsule3D shape, Vector3 displacement, QueryFilter3D? filter = null,
        int maximumIterations = 6, float minimumGroundNormalY = 0)
    {
        return SpatialMovement3D.SweepAndSlide(this, shape, displacement, filter, maximumIterations, minimumGroundNormalY);
    }

    private ImmutableArray<SpatialHit3D> OverlapBody(BodyState state, Capsule3D shape, QueryFilter3D? filter)
    {
        return state.Local.Overlap(ToLocal(shape, state), filter).Select(hit => ToWorld(hit, state)).ToImmutableArray();
    }

    private BodyState State(string id)
    {
        if (id is null || !bodies.TryGetValue(id, out BodyState? state))
        {
            throw new KeyNotFoundException($"No kinematic body '{id}'.");
        }
        return state;
    }

    private static Capsule3D ToLocal(Capsule3D shape, BodyState state) =>
        new(Vector3.Transform(shape.A, state.Inverse), Vector3.Transform(shape.B, state.Inverse), shape.Radius);

    private static SpatialHit3D ToWorld(SpatialHit3D hit, BodyState state) => hit with
    {
        Point = Vector3.Transform(hit.Point, state.Current),
        Normal = Vector3.Normalize(Vector3.TransformNormal(hit.Normal, state.Current)),
    };

    private static SpatialHit3D? Better(SpatialHit3D candidate, SpatialHit3D? current)
    {
        if (current is not { } existing)
        {
            return candidate;
        }
        // Initial overlaps outrank contacts; then nearest; then stable identity order.
        bool candidateOverlap = candidate.Status == SpatialQueryStatus3D.InitiallyOverlapping;
        bool existingOverlap = existing.Status == SpatialQueryStatus3D.InitiallyOverlapping;
        if (candidateOverlap != existingOverlap)
        {
            return candidateOverlap ? candidate : existing;
        }
        float delta = candidate.Distance - existing.Distance;
        if (MathF.Abs(delta) > SpatialMath3D.Epsilon)
        {
            return delta < 0 ? candidate : existing;
        }
        int identity = StringComparer.Ordinal.Compare(candidate.ColliderId, existing.ColliderId);
        return identity < 0 || (identity == 0 && candidate.TriangleIndex < existing.TriangleIndex) ? candidate : existing;
    }

    private static Matrix4x4 Invert(Matrix4x4 transform)
    {
        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse))
        {
            throw new ArgumentException("Kinematic transforms must be invertible.");
        }
        return inverse;
    }

    private static void ValidateRigid(Matrix4x4 transform)
    {
        Vector3 x = new(transform.M11, transform.M12, transform.M13);
        Vector3 y = new(transform.M21, transform.M22, transform.M23);
        Vector3 z = new(transform.M31, transform.M32, transform.M33);
        Vector3 translation = transform.Translation;
        SpatialMath3D.RequireFinite(x);
        SpatialMath3D.RequireFinite(y);
        SpatialMath3D.RequireFinite(z);
        SpatialMath3D.RequireFinite(translation);
        const float Tolerance = 1e-3f;
        if (MathF.Abs(x.Length() - 1) > Tolerance || MathF.Abs(y.Length() - 1) > Tolerance || MathF.Abs(z.Length() - 1) > Tolerance
            || MathF.Abs(Vector3.Dot(x, y)) > Tolerance || MathF.Abs(Vector3.Dot(y, z)) > Tolerance || MathF.Abs(Vector3.Dot(x, z)) > Tolerance
            || transform.GetDeterminant() <= 0 || transform.M14 != 0 || transform.M24 != 0 || transform.M34 != 0 || transform.M44 != 1)
        {
            throw new ArgumentException("Kinematic transforms must be rigid: an orthonormal rotation plus translation, without scale.");
        }
    }
}
