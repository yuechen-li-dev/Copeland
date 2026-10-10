using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Spatial3D;

namespace Aurelian.Physics3D;

public enum PhysicsMotionType3D
{
    Static,
    Kinematic,
    Dynamic,
}

/// <summary>Metres, kilograms and seconds. Capsule length excludes its spherical caps.</summary>
public abstract record PhysicsShape3D
{
    public sealed record Box(Vector3 Size) : PhysicsShape3D;
    public sealed record Sphere(float Radius) : PhysicsShape3D;
    public sealed record Capsule(float Radius, float Length) : PhysicsShape3D;
    public sealed record StaticMesh(CollisionMesh3D Geometry) : PhysicsShape3D;

    public void Validate(PhysicsMotionType3D motionType)
    {
        switch (this)
        {
            case Box box:
                PhysicsValidation3D.Finite(box.Size);
                if (box.Size.X <= 0 || box.Size.Y <= 0 || box.Size.Z <= 0)
                {
                    throw new ArgumentException("Physics box dimensions must be positive.");
                }
                break;
            case Sphere sphere:
                PhysicsValidation3D.Positive(sphere.Radius, nameof(sphere.Radius));
                break;
            case Capsule capsule:
                PhysicsValidation3D.Positive(capsule.Radius, nameof(capsule.Radius));
                PhysicsValidation3D.Positive(capsule.Length, nameof(capsule.Length));
                break;
            case StaticMesh mesh:
                ArgumentNullException.ThrowIfNull(mesh.Geometry);
                if (motionType != PhysicsMotionType3D.Static)
                {
                    throw new NotSupportedException("AUR-PHYSICS-001: Triangle meshes are static collision only; use convex primitives for mobile bodies.");
                }
                break;
            default:
                throw new NotSupportedException("Unsupported physics shape.");
        }
    }
}

public readonly record struct PhysicsPose3D(Vector3 Position, Quaternion Orientation)
{
    public static PhysicsPose3D At(Vector3 position) => new(position, Quaternion.Identity);
    public Matrix4x4 Matrix => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);

    public void Validate()
    {
        PhysicsValidation3D.Finite(Position);
        if (!float.IsFinite(Orientation.LengthSquared()) || MathF.Abs(Orientation.LengthSquared() - 1) > .0001f)
        {
            throw new ArgumentException("Physics orientation must be a finite unit quaternion.");
        }
    }
}

public readonly record struct PhysicsVelocity3D(Vector3 Linear, Vector3 Angular)
{
    public void Validate()
    {
        PhysicsValidation3D.Finite(Linear);
        PhysicsValidation3D.Finite(Angular);
    }
}

public sealed record PhysicsBody3D(string Id, PhysicsShape3D Shape, PhysicsPose3D Pose)
{
    public PhysicsMotionType3D MotionType { get; init; } = PhysicsMotionType3D.Dynamic;
    public PhysicsVelocity3D Velocity { get; init; }
    public float Mass { get; init; } = 1;
    public float Friction { get; init; } = .8f;
    public uint Layer { get; init; } = 1;
    public uint Mask { get; init; } = uint.MaxValue;
    public string? SemanticOwnerId { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(Shape);
        if (!Enum.IsDefined(MotionType) || Layer == 0)
        {
            throw new ArgumentException("Physics bodies require a known motion type and a nonzero collision layer.");
        }
        Shape.Validate(MotionType);
        Pose.Validate();
        Velocity.Validate();
        PhysicsValidation3D.Positive(Mass, nameof(Mass));
        if (!float.IsFinite(Friction) || Friction < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Friction));
        }
        if (MotionType == PhysicsMotionType3D.Static && Velocity != default)
        {
            throw new ArgumentException("Static physics bodies cannot have velocity.");
        }
    }
}

public sealed record PhysicsWorldOptions3D
{
    public Vector3 Gravity { get; init; } = new(0, -9.81f, 0);
    public float FixedDeltaSeconds { get; init; } = 1f / 60;
    public int WorkerCount { get; init; } = 1;
    public int SolverIterations { get; init; } = 8;
    public int Substeps { get; init; } = 1;
    public bool CollectContacts { get; init; } = true;
    public bool EnableSleeping { get; init; } = true;

    public void Validate()
    {
        PhysicsValidation3D.Finite(Gravity);
        PhysicsValidation3D.Positive(FixedDeltaSeconds, nameof(FixedDeltaSeconds));
        if (FixedDeltaSeconds > .1f || WorkerCount < 1 || WorkerCount > 64
            || SolverIterations < 1 || SolverIterations > 64 || Substeps < 1 || Substeps > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(PhysicsWorldOptions3D));
        }
    }
}

public sealed record PhysicsBodyState3D(string Id, string? SemanticOwnerId, PhysicsMotionType3D MotionType,
    PhysicsPose3D Pose, PhysicsVelocity3D Velocity, bool Awake);

/// <summary>Touching pairs observed during this solver step, not begin/end or solved impulse events.</summary>
public readonly record struct PhysicsContactPair3D(string BodyA, string BodyB);

public readonly record struct PhysicsStepRequest3D(long Tick);
public sealed record PhysicsStepResult3D(long Tick, ImmutableArray<PhysicsContactPair3D> Contacts);
public sealed record PhysicsSnapshot3D(string Backend, long Tick, ImmutableArray<PhysicsBodyState3D> Bodies);
public readonly record struct PhysicsRayHit3D(string BodyId, string? SemanticOwnerId, float Distance,
    Vector3 Point, Vector3 Normal);

/// <summary>Explicit, thread-confined owner. Call Step once per physics cadence; rendering never advances it.</summary>
public interface IPhysicsWorld3D : IDisposable
{
    string Backend { get; }
    PhysicsWorldOptions3D Options { get; }
    long Tick { get; }
    int BodyCount { get; }
    void AddBody(PhysicsBody3D body);
    bool RemoveBody(string id);
    PhysicsBodyState3D GetBody(string id);
    void SetMotion(string id, PhysicsPose3D pose, PhysicsVelocity3D velocity);
    void ApplyImpulse(string id, Vector3 impulse, Vector3 worldOffset = default);
    PhysicsStepResult3D Step(PhysicsStepRequest3D request);
    PhysicsSnapshot3D CaptureSnapshot();
    PhysicsRayHit3D? Raycast(Ray3D ray, QueryFilter3D? filter = null);
}

internal static class PhysicsValidation3D
{
    public static void Finite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentException("Physics vectors must be finite.");
        }
    }

    public static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
