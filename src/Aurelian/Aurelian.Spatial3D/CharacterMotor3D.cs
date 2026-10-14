using System.Numerics;

namespace Aurelian.Spatial3D;

/// <param name="StepHeight">
/// Maximum ledge a grounded character climbs without jumping (stairs, kerbs). Zero disables stepping.
/// </param>
public sealed record CharacterMotorOptions3D(float Radius = 0.3f, float Height = 1.8f,
    float Gravity = 12, float JumpSpeed = 5, float MaximumSlopeDegrees = 45, float GroundSnap = 0.08f,
    float StepHeight = 0)
{
    public void Validate()
    {
        Capsule3D.AtFeet(Vector3.Zero, Radius, Height).Validate();
        if (!float.IsFinite(Gravity) || Gravity <= 0 || !float.IsFinite(JumpSpeed) || JumpSpeed < 0
            || !float.IsFinite(MaximumSlopeDegrees) || MaximumSlopeDegrees < 0 || MaximumSlopeDegrees >= 90
            || !float.IsFinite(GroundSnap) || GroundSnap <= 0 || GroundSnap > Radius
            || !float.IsFinite(StepHeight) || StepHeight < 0 || StepHeight > Height - 2 * Radius)
        {
            throw new ArgumentException("Motor settings need finite dimensions, gravity, jump, a slope below 90 degrees "
                + "and a step height between zero and the capsule's straight section.");
        }
    }
}

public readonly record struct CharacterState3D(Vector3 Feet, float VerticalVelocity);
public sealed record CharacterMove3D(CharacterState3D State, bool Grounded, Vector3 GroundNormal,
    string? GroundColliderId, MoveResult3D Movement)
{
    /// <summary>Accepted displacement from an external carry (moving platform or push), applied before the step.</summary>
    public Vector3 Carried { get; init; }

    /// <summary>Height gained by stepping onto a ledge this step; zero when no step was taken.</summary>
    public float SteppedUp { get; init; }

    /// <summary>True when the character ended the step inside geometry (for example crushed by a platform).</summary>
    public bool Embedded => Movement.InitiallyOverlapping;
}

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

    /// <param name="carry">
    /// Displacement imposed from outside before the character's own movement, typically from
    /// <see cref="KinematicSpatialWorld3D.Carry"/>. It is swept against <paramref name="carryWorld"/>
    /// (default: <paramref name="world"/>) so a carried character cannot be pushed through static geometry.
    /// </param>
    public CharacterMove3D Step(ISpatialQueryWorld3D world, CharacterState3D state, Vector3 horizontalVelocity,
        bool jump, float seconds, QueryFilter3D? filter = null, float jumpVelocityOffset = 0,
        Vector3 carry = default, ISpatialQueryWorld3D? carryWorld = null)
    {
        SpatialMath3D.RequireFinite(state.Feet);
        SpatialMath3D.RequireFinite(horizontalVelocity);
        SpatialMath3D.RequireFinite(carry);
        if (!float.IsFinite(state.VerticalVelocity) || !float.IsFinite(jumpVelocityOffset)
            || !float.IsFinite(seconds) || seconds <= 0 || seconds > 0.05f
            || MathF.Abs(horizontalVelocity.Y) > 1e-6f)
        {
            throw new ArgumentException("Motor steps need finite horizontal velocity and a positive timestep of at most 50 ms.");
        }
        Vector3 carried = Vector3.Zero;
        if (carry.LengthSquared() > 1e-12f)
        {
            Capsule3D before = Capsule3D.AtFeet(state.Feet, Options.Radius, Options.Height);
            MoveResult3D carriedMove = (carryWorld ?? world).SweepAndSlide(before, carry, filter);
            carried = carriedMove.InitiallyOverlapping ? carry : carriedMove.AcceptedDisplacement;
            state = state with { Feet = state.Feet + carried };
        }
        CharacterMove3D result = StepFrom(world, state, horizontalVelocity, jump, seconds, filter, jumpVelocityOffset);
        return result with { Carried = carried };
    }

    private CharacterMove3D StepFrom(ISpatialQueryWorld3D world, CharacterState3D state, Vector3 horizontalVelocity,
        bool jump, float seconds, QueryFilter3D? filter, float jumpVelocityOffset)
    {
        SpatialHit3D? ground = ProbeGround(world, state.Feet, filter);
        bool grounded = ground is { Normal.Y: var y } && y >= groundNormalY && state.VerticalVelocity <= 0;
        bool startedGrounded = grounded;
        float vertical = grounded ? 0 : state.VerticalVelocity;
        Vector3 velocity = horizontalVelocity;
        if (grounded && jump)
        {
            vertical = Options.JumpSpeed + jumpVelocityOffset;
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
        float steppedUp = 0;
        if (startedGrounded && !move.InitiallyOverlapping
            && TryStep(world, state.Feet, horizontalVelocity * seconds, move, filter) is { } stepped)
        {
            steppedUp = stepped.Y - state.Feet.Y;
            feet = stepped;
            move = move with { AcceptedDisplacement = stepped - state.Feet };
        }
        if (vertical > 0 && move.Contacts.Any(contact => contact.Normal.Y < -0.1f)) vertical = 0;
        SpatialHit3D? landed = vertical <= 0 && !move.InitiallyOverlapping ? ProbeGround(world, feet, filter) : null;
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
            grounded ? landed!.Value.ColliderId : null, move)
        {
            SteppedUp = steppedUp,
        };
    }

    /// <summary>
    /// Up, forward, down. Taken only when a grounded move lost horizontal progress to a wall-like contact and
    /// walkable ground exists within <see cref="CharacterMotorOptions3D.StepHeight"/> above the feet.
    /// </summary>
    private Vector3? TryStep(ISpatialQueryWorld3D world, Vector3 feet, Vector3 requested, MoveResult3D plain,
        QueryFilter3D? filter)
    {
        float step = Options.StepHeight;
        Vector3 horizontal = new(requested.X, 0, requested.Z);
        float length = horizontal.Length();
        if (step <= 0 || length < 1e-5f)
        {
            return null;
        }
        Vector3 direction = horizontal / length;
        float plainProgress = Vector3.Dot(new Vector3(plain.AcceptedDisplacement.X, 0, plain.AcceptedDisplacement.Z), direction);
        bool blocked = plain.Contacts.Any(contact => contact.Normal.Y < groundNormalY && Vector3.Dot(contact.Normal, direction) < -0.1f);
        if (!blocked || plainProgress >= length - 1e-4f)
        {
            return null;
        }
        // The capsule's lower sphere must travel far enough past the ledge's corner that the corner
        // presents a walkable normal; slow approaches advance by that minimum instead of stalling.
        float minimumAdvance = Options.Radius * (1 - MathF.Sin(Options.MaximumSlopeDegrees * MathF.PI / 180)) + 0.005f;
        float advance = MathF.Max(length, minimumAdvance);
        Capsule3D capsule = Capsule3D.AtFeet(feet, Options.Radius, Options.Height);
        SpatialHit3D? ceiling = world.Sweep(capsule, Vector3.UnitY * step, filter);
        if (ceiling is { Status: SpatialQueryStatus3D.InitiallyOverlapping })
        {
            return null;
        }
        float rise = ceiling is { } roof ? MathF.Max(0, roof.TimeOfImpact * step - 0.001f) : step;
        if (rise <= 1e-4f)
        {
            return null;
        }
        Capsule3D raised = capsule.Translated(Vector3.UnitY * rise);
        MoveResult3D across = world.SweepAndSlide(raised, direction * advance, filter, minimumGroundNormalY: groundNormalY);
        if (across.InitiallyOverlapping)
        {
            return null;
        }
        Capsule3D ahead = raised.Translated(across.AcceptedDisplacement);
        float drop = rise + Options.GroundSnap;
        SpatialHit3D? floor = world.Sweep(ahead, -Vector3.UnitY * drop, filter);
        if (floor is not { Status: SpatialQueryStatus3D.Contact } landing || landing.Normal.Y < groundNormalY)
        {
            return null;
        }
        Vector3 candidate = feet + Vector3.UnitY * rise + across.AcceptedDisplacement
            - Vector3.UnitY * MathF.Max(0, landing.TimeOfImpact * drop - 0.0005f);
        float gained = candidate.Y - feet.Y;
        float progress = Vector3.Dot(new Vector3(candidate.X - feet.X, 0, candidate.Z - feet.Z), direction);
        if (gained <= 1e-4f || gained > step + 1e-4f || progress <= plainProgress + 1e-4f)
        {
            return null;
        }
        // Resting on a ledge corner leaves the feet slightly below its top. The character must also fit when standing
        // on the top itself, otherwise a low ceiling would wedge it on the corner.
        Vector3 standing = candidate with { Y = MathF.Max(candidate.Y, landing.Point.Y) };
        if (standing.Y - feet.Y > step + 1e-4f
            || !world.Overlap(Capsule3D.AtFeet(candidate, Options.Radius, Options.Height), filter).IsEmpty
            || !world.Overlap(Capsule3D.AtFeet(standing, Options.Radius, Options.Height), filter).IsEmpty)
        {
            return null;
        }
        return candidate;
    }

    public SpatialHit3D? ProbeGround(ISpatialQueryWorld3D world, Vector3 feet, QueryFilter3D? filter = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        SpatialMath3D.RequireFinite(feet);
        Capsule3D raised = Capsule3D.AtFeet(feet + Vector3.UnitY * 0.001f, Options.Radius, Options.Height);
        return world.Sweep(raised, -Vector3.UnitY * (Options.GroundSnap + 0.001f), filter);
    }
}
