using System.Numerics;

namespace Aurelian.Spatial3D;

public sealed record CharacterMotorOptions3D(float Radius = 0.3f, float Height = 1.8f,
    float Gravity = 12, float JumpSpeed = 5, float MaximumSlopeDegrees = 45, float GroundSnap = 0.08f)
{
    public void Validate()
    {
        Capsule3D.AtFeet(Vector3.Zero, Radius, Height).Validate();
        if (!float.IsFinite(Gravity) || Gravity <= 0 || !float.IsFinite(JumpSpeed) || JumpSpeed < 0
            || !float.IsFinite(MaximumSlopeDegrees) || MaximumSlopeDegrees < 0 || MaximumSlopeDegrees >= 90
            || !float.IsFinite(GroundSnap) || GroundSnap <= 0 || GroundSnap > Radius)
        {
            throw new ArgumentException("Motor settings need finite dimensions, gravity, jump and a slope below 90 degrees.");
        }
    }
}

public readonly record struct CharacterState3D(Vector3 Feet, float VerticalVelocity);
public sealed record CharacterMove3D(CharacterState3D State, bool Grounded, Vector3 GroundNormal,
    string? GroundColliderId, MoveResult3D Movement);

/// <summary>Pure accepted-movement calculation. State and timestep belong to the caller.</summary>
public sealed class CharacterMotor3D
{
    public CharacterMotorOptions3D Options { get; }
    private readonly float groundNormalY;

    public CharacterMotor3D(CharacterMotorOptions3D? options = null)
    {
        Options = options ?? new();
        Options.Validate();
        groundNormalY = MathF.Cos(Options.MaximumSlopeDegrees * MathF.PI / 180);
    }

    public CharacterMove3D Step(ISpatialQueryWorld3D world, CharacterState3D state, Vector3 horizontalVelocity,
        bool jump, float seconds, QueryFilter3D? filter = null)
    {
        SpatialMath3D.RequireFinite(state.Feet);
        SpatialMath3D.RequireFinite(horizontalVelocity);
        if (!float.IsFinite(state.VerticalVelocity) || !float.IsFinite(seconds) || seconds <= 0 || seconds > 0.05f
            || MathF.Abs(horizontalVelocity.Y) > 1e-6f)
        {
            throw new ArgumentException("Motor steps need finite horizontal velocity and a positive timestep of at most 50 ms.");
        }
        SpatialHit3D? ground = Ground(world, state.Feet, filter);
        bool grounded = ground is { Normal.Y: var y } && y >= groundNormalY && state.VerticalVelocity <= 0;
        float vertical = grounded ? 0 : state.VerticalVelocity;
        Vector3 velocity = horizontalVelocity;
        if (grounded && jump)
        {
            vertical = Options.JumpSpeed;
            grounded = false;
        }
        if (grounded)
        {
            Vector3 normal = ground!.Value.Normal;
            velocity.Y = -(normal.X * velocity.X + normal.Z * velocity.Z) / normal.Y;
        }
        else
        {
            vertical -= Options.Gravity * seconds;
        }
        velocity.Y += vertical;
        Capsule3D capsule = Capsule3D.AtFeet(state.Feet, Options.Radius, Options.Height);
        MoveResult3D move = world.SweepAndSlide(capsule, velocity * seconds, filter, minimumGroundNormalY: groundNormalY);
        Vector3 feet = state.Feet + move.AcceptedDisplacement;
        if (vertical > 0 && move.Contacts.Any(contact => contact.Normal.Y < -0.1f)) vertical = 0;
        SpatialHit3D? landed = vertical <= 0 && !move.InitiallyOverlapping ? Ground(world, feet, filter) : null;
        grounded = landed is { Normal.Y: var landedY } && landedY >= groundNormalY;
        if (grounded)
        {
            SpatialHit3D contact = landed!.Value;
            Vector3 centre = feet + Vector3.UnitY * Options.Radius;
            float gap = Vector3.Dot(centre - contact.Point, contact.Normal) - Options.Radius;
            feet -= Vector3.UnitY * MathF.Max(0, gap / contact.Normal.Y);
            if (contact.Normal.Y > 0.999999f) feet.Y = contact.Point.Y;
            vertical = 0;
        }
        return new(new(feet, vertical), grounded, grounded ? landed!.Value.Normal : Vector3.Zero,
            grounded ? landed!.Value.ColliderId : null, move);
    }

    private SpatialHit3D? Ground(ISpatialQueryWorld3D world, Vector3 feet, QueryFilter3D? filter)
    {
        Capsule3D raised = Capsule3D.AtFeet(feet + Vector3.UnitY * 0.001f, Options.Radius, Options.Height);
        return world.Sweep(raised, -Vector3.UnitY * (Options.GroundSnap + 0.001f), filter);
    }
}
