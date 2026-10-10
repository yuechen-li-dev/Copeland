using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Spatial3D;

namespace Aurelian.Physics3D;

public sealed record PhysicsCharacterOptions3D
{
    public CharacterMotorOptions3D Movement { get; init; } = new();
    public uint Layer { get; init; } = 2;
    public uint Mask { get; init; } = uint.MaxValue;
    public float MaximumPushForce { get; init; } = 200;
    public float PushSpeed { get; init; } = 4;

    public void Validate()
    {
        Movement.Validate();
        if (Movement.Height <= 2 * Movement.Radius)
        {
            throw new ArgumentException("Physics characters require a capsule height greater than its diameter.");
        }
        if (Layer == 0 || !float.IsFinite(MaximumPushForce) || MaximumPushForce < 0)
        {
            throw new ArgumentException("Character layer and nonnegative push force must be valid.");
        }
        PhysicsValidation3D.Positive(PushSpeed, nameof(PushSpeed));
    }
}

public readonly record struct PhysicsCharacterInput3D(long Tick, Vector3 HorizontalVelocity, bool Jump = false);
public readonly record struct PhysicsCharacterPush3D(string BodyId, Vector3 Impulse, Vector3 WorldPoint);
public sealed record PhysicsCharacterState3D(string BodyId, long Tick, CharacterState3D Motion,
    bool Grounded, Vector3 GroundNormal, string? SupportBodyId, PhysicsPose3D? SupportPose,
    Vector3 SupportLocalFeet, Vector3 CarrierVelocity, Vector3 AirborneVelocity)
{
    public MoveResult3D? Movement { get; init; }
    public MoveResult3D? CarrierMovement { get; init; }
    public ImmutableArray<PhysicsCharacterPush3D> Pushes { get; init; } = [];
}

/// <summary>
/// Call after the world's fixed step, then publish. Carrier motion uses resolved poses;
/// accepted kinematic placement is immediate, and bounded push impulses affect the next solver step.
/// State is explicit caller/agent data; this controller never advances physics.
/// </summary>
public sealed class PhysicsCharacterMotor3D
{
    private readonly CharacterMotor3D motor;
    private readonly float minimumGroundNormalY;
    public PhysicsCharacterOptions3D Options { get; }

    public PhysicsCharacterMotor3D(PhysicsCharacterOptions3D? options = null)
    {
        Options = options ?? new();
        Options.Validate();
        motor = new(Options.Movement);
        minimumGroundNormalY = MathF.Cos(Options.Movement.MaximumSlopeDegrees * MathF.PI / 180);
    }

    public PhysicsBody3D Body(string id, Vector3 feet)
    {
        return new(id, new PhysicsShape3D.Capsule(Options.Movement.Radius,
            Options.Movement.Height - 2 * Options.Movement.Radius), PhysicsPose3D.At(feet + CenterOffset))
        {
            MotionType = PhysicsMotionType3D.Kinematic,
            Friction = 0,
            Layer = Options.Layer,
            Mask = Options.Mask,
            SemanticOwnerId = id,
        };
    }

    public PhysicsCharacterState3D Initialize(IPhysicsWorld3D world, string id)
    {
        ValidateWorld(world, id);
        Vector3 feet = world.GetBody(id).Pose.Position - CenterOffset;
        var query = world.CreateQueryWorld(id);
        if (!query.Overlap(Shape(feet), Filter).IsEmpty)
        {
            throw new InvalidOperationException("AUR-CHARACTER-001: Character starts inside collision geometry; author a clear spawn or explicitly relocate it.");
        }
        var ground = motor.ProbeGround(query, feet, Filter);
        bool grounded = ground is { Normal.Y: var y } && y >= minimumGroundNormalY;
        return Supported(world, new(id, world.Tick, new(feet, 0), grounded,
            grounded ? ground!.Value.Normal : Vector3.Zero, grounded ? ground!.Value.ColliderId : null,
            null, default, default, default));
    }

    public PhysicsCharacterState3D Move(IPhysicsWorld3D world, PhysicsCharacterState3D state, PhysicsCharacterInput3D input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateWorld(world, state.BodyId);
        if (input.Tick != world.Tick || input.Tick != checked(state.Tick + 1))
        {
            throw new ArgumentException("AUR-CHARACTER-002: Move once after each matching physics tick; replay or reinitialize after a cadence gap.");
        }
        PhysicsValidation3D.Finite(input.HorizontalVelocity);
        PhysicsValidation3D.Finite(state.AirborneVelocity);
        PhysicsValidation3D.Finite(state.CarrierVelocity);
        PhysicsValidation3D.Finite(state.Motion.Feet);
        if (!float.IsFinite(state.Motion.VerticalVelocity))
        {
            throw new ArgumentException("Character vertical velocity must be finite.");
        }
        state.SupportPose?.Validate();
        PhysicsValidation3D.Finite(state.SupportLocalFeet);
        if (Math.Abs(input.HorizontalVelocity.Y) > 1e-6f)
        {
            throw new ArgumentException("Character input velocity must be horizontal.");
        }
        PhysicsBodyState3D body = world.GetBody(state.BodyId);
        Vector3 feet = body.Pose.Position - CenterOffset;
        if (Vector3.DistanceSquared(feet, state.Motion.Feet) > 1e-8f || body.Velocity != default
            || MathF.Abs(Quaternion.Dot(body.Pose.Orientation, Quaternion.Identity)) < .99999f)
        {
            throw new InvalidOperationException("AUR-CHARACTER-003: Body and character state diverged; reinitialize after external relocation.");
        }
        var query = world.CreateQueryWorld(state.BodyId);
        float seconds = world.Options.FixedDeltaSeconds;
        Vector3 carrierVelocity = Vector3.Zero;
        MoveResult3D? carry = null;
        bool hasCarrier = state.Grounded && state.SupportBodyId is not null && state.SupportPose is not null
            && world.TryGetBody(state.SupportBodyId, out _);
        if (hasCarrier)
        {
            PhysicsPose3D support = world.GetBody(state.SupportBodyId!).Pose;
            PhysicsPose3D previousPose = state.SupportPose!.Value;
            // Carry the bottom sphere's centre, then recover upright feet. Rotating the foot
            // point alone embeds an upright capsule when its support pitches or rolls.
            Vector3 localUp = Vector3.Transform(Vector3.UnitY, Quaternion.Conjugate(previousPose.Orientation));
            Vector3 localCenter = state.SupportLocalFeet + localUp * Options.Movement.Radius;
            Vector3 previous = Vector3.Transform(localCenter, previousPose.Matrix);
            Vector3 current = Vector3.Transform(localCenter, support.Matrix);
            Vector3 displacement = current - previous;
            carrierVelocity = displacement / seconds;
            carry = world.CreateQueryWorld(state.BodyId, state.SupportBodyId!).SweepAndSlide(Shape(feet), displacement,
                Filter, minimumGroundNormalY: minimumGroundNormalY);
            feet += carry.AcceptedDisplacement;
        }
        Vector3 airborne = state.AirborneVelocity;
        if (!hasCarrier && state.Grounded)
        {
            airborne = state.CarrierVelocity;
        }
        var groundBefore = motor.ProbeGround(query, feet, Filter);
        bool canJump = groundBefore is { Normal.Y: var normalY } && normalY >= minimumGroundNormalY
            && state.Motion.VerticalVelocity <= 0;
        if (input.Jump && canJump)
        {
            airborne = carrierVelocity;
        }
        Vector3 inheritedHorizontal = new(airborne.X, 0, airborne.Z);
        var initial = state.Motion with { Feet = feet };
        if (!hasCarrier && state.Grounded)
        {
            initial = initial with { VerticalVelocity = airborne.Y };
        }
        CharacterMove3D move = motor.Step(query, initial, input.HorizontalVelocity + inheritedHorizontal,
            input.Jump, seconds, Filter, jumpVelocityOffset: carrierVelocity.Y);
        if (hasCarrier && !move.Grounded && !(input.Jump && canJump))
        {
            airborne = carrierVelocity;
            move = move with { State = move.State with { VerticalVelocity = move.State.VerticalVelocity + carrierVelocity.Y } };
        }
        if (move.Grounded)
        {
            airborne = Vector3.Zero;
        }
        var pushes = ImmutableArray.CreateBuilder<PhysicsCharacterPush3D>();
        foreach (SpatialHit3D contact in move.Movement.Contacts.DistinctBy(contact => contact.ColliderId))
        {
            if (!world.TryGetBody(contact.ColliderId, out var target) || target!.MotionType != PhysicsMotionType3D.Dynamic
                || contact.Status != SpatialQueryStatus3D.Contact || contact.Normal.Y >= minimumGroundNormalY)
            {
                continue;
            }
            Vector3 horizontalNormal = new(contact.Normal.X, 0, contact.Normal.Z);
            if (horizontalNormal.LengthSquared() < 1e-8f) continue;
            horizontalNormal = Vector3.Normalize(horizontalNormal);
            float closingSpeed = Math.Max(0, -Vector3.Dot(input.HorizontalVelocity + carrierVelocity + inheritedHorizontal
                - target.Velocity.Linear, horizontalNormal));
            float impulseMagnitude = Options.MaximumPushForce * seconds * Math.Clamp(closingSpeed / Options.PushSpeed, 0, 1);
            if (impulseMagnitude <= 0) continue;
            pushes.Add(new(target.Id, -horizontalNormal * impulseMagnitude, contact.Point));
        }
        var next = Supported(world, new(state.BodyId, input.Tick, move.State, move.Grounded, move.GroundNormal,
            move.GroundColliderId, null, default, carrierVelocity, airborne)
        {
            Movement = move.Movement,
            CarrierMovement = carry,
            Pushes = pushes.ToImmutable(),
        });
        // Validation/calculation precedes mutation. The one world remains authoritative for accepted poses.
        world.SetMotion(state.BodyId, PhysicsPose3D.At(move.State.Feet + CenterOffset), default);
        foreach (PhysicsCharacterPush3D push in next.Pushes)
        {
            world.ApplyImpulse(push.BodyId, push.Impulse, push.WorldPoint - world.GetBody(push.BodyId).Pose.Position);
        }
        return next;
    }

    private PhysicsCharacterState3D Supported(IPhysicsWorld3D world, PhysicsCharacterState3D state)
    {
        if (!state.Grounded || state.SupportBodyId is null) return state;
        PhysicsPose3D pose = world.GetBody(state.SupportBodyId).Pose;
        Matrix4x4.Invert(pose.Matrix, out Matrix4x4 inverse);
        return state with { SupportPose = pose, SupportLocalFeet = Vector3.Transform(state.Motion.Feet, inverse) };
    }

    private void ValidateWorld(IPhysicsWorld3D world, string id)
    {
        ArgumentNullException.ThrowIfNull(world);
        PhysicsBody3D body = world.GetBodyDescription(id);
        var expectedShape = new PhysicsShape3D.Capsule(Options.Movement.Radius,
            Options.Movement.Height - 2 * Options.Movement.Radius);
        if (world.Options.FixedDeltaSeconds > .05f || body.MotionType != PhysicsMotionType3D.Kinematic
            || body.Shape != expectedShape || body.Layer != Options.Layer || body.Mask != Options.Mask)
        {
            throw new InvalidOperationException("Characters require an owned kinematic capsule and a fixed step of at most 50 ms.");
        }
    }

    private Vector3 CenterOffset => Vector3.UnitY * (Options.Movement.Height * .5f);
    private Capsule3D Shape(Vector3 feet) => Capsule3D.AtFeet(feet, Options.Movement.Radius, Options.Movement.Height);
    private QueryFilter3D Filter => new(Options.Mask, Options.Layer);
}
