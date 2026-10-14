using System.Numerics;
using Aurelian.Spatial3D;

namespace Cloudstep;

/// <summary>Tuning for the hero. Values are per second unless noted.</summary>
public sealed record PlayerTuning
{
    public float RunSpeed { get; init; } = 7;
    public float GroundAcceleration { get; init; } = 48;
    public float GroundDeceleration { get; init; } = 60;
    public float AirAcceleration { get; init; } = 22;
    public float JumpSpeed { get; init; } = 9;
    public float Gravity { get; init; } = 28;
    public float FallSpeedLimit { get; init; } = 30;

    /// <summary>Releasing jump while still rising multiplies upward speed by this, for short hops.</summary>
    public float JumpCut { get; init; } = 0.45f;

    /// <summary>Seconds after leaving a ledge during which a jump still counts.</summary>
    public float CoyoteTime { get; init; } = 0.1f;

    /// <summary>Seconds a jump pressed before landing is remembered.</summary>
    public float JumpBuffer { get; init; } = 0.12f;
    public float TurnRate { get; init; } = 14;

    public CharacterMotorOptions3D Motor => new(Radius: 0.35f, Height: 1.4f, Gravity: Gravity, JumpSpeed: JumpSpeed,
        MaximumSlopeDegrees: 50, GroundSnap: 0.12f, StepHeight: 0.4f);
}

/// <summary>The hero's simulation state: everything a save needs to reproduce the next tick exactly.</summary>
public sealed record PlayerSnapshot(
    float X, float Y, float Z,
    float VelocityX, float VerticalVelocity, float VelocityZ,
    float Facing,
    string? Support,
    bool Grounded,
    float Coyote,
    float Buffer,
    bool Rising);

public readonly record struct PlayerInput(Vector2 Move, bool JumpPressed, bool JumpHeld);

public readonly record struct PlayerStepResult(bool Jumped, bool Landed, float LandingSpeed, bool Crushed, float SteppedUp);

/// <summary>
/// Platformer movement on top of <see cref="CharacterMotor3D"/>: acceleration, air control, coyote time, jump buffering,
/// variable jump height, platform carry and momentum. Camera-relative input arrives already rotated into world XZ.
/// </summary>
public sealed class CloudstepPlayer
{
    private readonly CharacterMotor3D motor;
    private Vector3 feet;
    private Vector2 velocity;
    private float vertical;
    private string? support;
    private bool grounded;
    private float coyote;
    private float buffer;
    private bool rising;

    public CloudstepPlayer(PlayerTuning? tuning = null)
    {
        Tuning = tuning ?? new PlayerTuning();
        motor = new CharacterMotor3D(Tuning.Motor);
    }

    public PlayerTuning Tuning { get; }
    public CharacterMotorOptions3D Motor => motor.Options;
    public Vector3 Feet => feet;
    public Vector3 Centre => feet + Vector3.UnitY * (motor.Options.Height / 2);
    public Vector2 Velocity => velocity;
    public float VerticalVelocity => vertical;
    public float Facing { get; private set; }
    public bool Grounded => grounded;
    public string? Support => support;
    public Capsule3D Capsule => Capsule3D.AtFeet(feet, motor.Options.Radius, motor.Options.Height);

    public void Place(Vector3 position, float facing)
    {
        feet = position;
        Facing = facing;
        velocity = Vector2.Zero;
        vertical = 0;
        support = null;
        grounded = false;
        coyote = 0;
        buffer = 0;
        rising = false;
    }

    public PlayerSnapshot Capture() => new(feet.X, feet.Y, feet.Z, velocity.X, vertical, velocity.Y, Facing, support, grounded,
        coyote, buffer, rising);

    public void Restore(PlayerSnapshot snapshot)
    {
        feet = new(snapshot.X, snapshot.Y, snapshot.Z);
        velocity = new(snapshot.VelocityX, snapshot.VelocityZ);
        vertical = snapshot.VerticalVelocity;
        Facing = snapshot.Facing;
        support = snapshot.Support;
        grounded = snapshot.Grounded;
        coyote = snapshot.Coyote;
        buffer = snapshot.Buffer;
        rising = snapshot.Rising;
    }

    public static void Validate(PlayerSnapshot snapshot)
    {
        float[] values = [snapshot.X, snapshot.Y, snapshot.Z, snapshot.VelocityX, snapshot.VerticalVelocity, snapshot.VelocityZ,
            snapshot.Facing, snapshot.Coyote, snapshot.Buffer];
        if (values.Any(value => !float.IsFinite(value)) || snapshot.Coyote < 0 || snapshot.Buffer < 0)
        {
            throw new InvalidDataException("Invalid player state.");
        }
    }

    public PlayerStepResult Step(KinematicSpatialWorld3D world, PlayerInput input, float seconds)
    {
        Vector2 move = input.Move;
        if (move.LengthSquared() > 1)
        {
            move = Vector2.Normalize(move);
        }

        // Ride whatever we stood on last tick, and get shoved by anything that moved into us.
        KinematicCarry3D carry = world.Carry(Capsule, support);
        // Carry yaw is counter-clockwise about +Y; facing yaw (like Camera3D) turns clockwise from -Z.
        Facing = MathF.IEEERemainder(Facing - carry.Yaw, MathF.Tau);
        Vector3 platformVelocity = carry.SupportId is { } riding ? world.PointVelocity(riding, feet, seconds) : Vector3.Zero;

        // Horizontal control.
        Vector2 target = move * Tuning.RunSpeed;
        float rate = grounded
            ? (move.LengthSquared() > 1e-4f ? Tuning.GroundAcceleration : Tuning.GroundDeceleration)
            : Tuning.AirAcceleration;
        velocity = MoveTowards(velocity, target, rate * seconds);
        if (move.LengthSquared() > 1e-4f)
        {
            float desired = MathF.Atan2(move.X, -move.Y);
            float delta = MathF.IEEERemainder(desired - Facing, MathF.Tau);
            float turn = Tuning.TurnRate * seconds;
            Facing = MathF.IEEERemainder(Facing + Math.Clamp(delta, -turn, turn), MathF.Tau);
        }

        // Jumping: buffered presses, coyote time and inherited platform momentum.
        buffer = input.JumpPressed ? Tuning.JumpBuffer : MathF.Max(0, buffer - seconds);
        coyote = grounded ? Tuning.CoyoteTime : MathF.Max(0, coyote - seconds);
        bool jumped = false;
        if (buffer > 0 && coyote > 0)
        {
            jumped = true;
            buffer = 0;
            coyote = 0;
            rising = true;
            vertical = Tuning.JumpSpeed + MathF.Max(0, platformVelocity.Y);
            velocity += new Vector2(platformVelocity.X, platformVelocity.Z);
            grounded = false;
        }
        if (rising && !input.JumpHeld && vertical > 0)
        {
            vertical *= Tuning.JumpCut;
            rising = false;
        }
        if (vertical <= 0)
        {
            rising = false;
        }

        bool wasGrounded = grounded;
        float fallSpeed = -vertical;
        CharacterMove3D result = motor.Step(world, new CharacterState3D(feet, vertical), new Vector3(velocity.X, 0, velocity.Y),
            jump: false, seconds, carry: carry.Displacement, carryWorld: world.Statics);
        feet = result.State.Feet;
        vertical = MathF.Max(result.State.VerticalVelocity, -Tuning.FallSpeedLimit);
        grounded = result.Grounded;
        support = result.GroundColliderId;

        // Walls absorb the velocity pushed into them, so speed does not build up against a wall.
        foreach (SpatialHit3D contact in result.Movement.Contacts)
        {
            var normal = new Vector2(contact.Normal.X, contact.Normal.Z);
            if (contact.Normal.Y < 0.5f && normal.LengthSquared() > 1e-6f)
            {
                normal = Vector2.Normalize(normal);
                float into = Vector2.Dot(velocity, normal);
                if (into < 0)
                {
                    velocity -= normal * into;
                }
            }
        }
        bool crushed = result.Embedded || !world.Overlap(Capsule).IsEmpty;
        return new PlayerStepResult(jumped, grounded && !wasGrounded, fallSpeed, crushed, result.SteppedUp);
    }

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maximumDelta)
    {
        Vector2 difference = target - current;
        float length = difference.Length();
        if (length <= maximumDelta || length < 1e-6f)
        {
            return target;
        }
        return current + difference / length * maximumDelta;
    }
}
