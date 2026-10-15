using System.Numerics;

namespace Aurelian.Spatial3D;

/// <param name="StepHeight">
/// Maximum ledge a grounded character climbs without jumping (stairs, kerbs). Zero disables stepping.
/// </param>
/// <param name="StepDownHeight">
/// While grounded, follow the ground down by up to this much (stairs, small drops) instead of briefly becoming airborne.
/// Zero uses <see cref="GroundSnap"/>.
/// </param>
/// <param name="LedgeAssistHeight">
/// Platformer forgiveness: an airborne character that is falling or near its apex and pushes into a ledge whose top is
/// within this height above its feet is lifted onto it. Zero disables the assist.
/// </param>
public sealed record CharacterMotorOptions3D(float Radius = 0.3f, float Height = 1.8f,
    float Gravity = 12, float JumpSpeed = 5, float MaximumSlopeDegrees = 45, float GroundSnap = 0.08f,
    float StepHeight = 0, float StepDownHeight = 0, float LedgeAssistHeight = 0)
{
    /// <summary>Ledge assist applies only while the upward speed is at most this (metres per second).</summary>
    public float LedgeAssistMaximumRise { get; init; } = 2;

    public void Validate()
    {
        Capsule3D.AtFeet(Vector3.Zero, Radius, Height).Validate();
        if (!float.IsFinite(Gravity) || Gravity <= 0 || !float.IsFinite(JumpSpeed) || JumpSpeed < 0
            || !float.IsFinite(MaximumSlopeDegrees) || MaximumSlopeDegrees < 0 || MaximumSlopeDegrees >= 90
            || !float.IsFinite(GroundSnap) || GroundSnap <= 0 || GroundSnap > Radius
            || !float.IsFinite(StepHeight) || StepHeight < 0 || StepHeight > Height - 2 * Radius
            || !float.IsFinite(StepDownHeight) || StepDownHeight < 0 || StepDownHeight > Height
            || !float.IsFinite(LedgeAssistHeight) || LedgeAssistHeight < 0 || LedgeAssistHeight > Height - 2 * Radius
            || !float.IsFinite(LedgeAssistMaximumRise))
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

    /// <summary>
    /// When grounded: whether there is ground directly beneath the capsule's axis. False means the character is resting
    /// on a ledge corner with its centre over a drop, which platformers usually resolve by letting it slip off.
    /// </summary>
    public bool CentreSupported { get; init; }

    /// <summary>True when the ledge assist lifted an airborne character onto a ledge this step.</summary>
    public bool LedgeAssisted { get; init; }
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
        state = state with { Feet = Depenetrate(world, state.Feet, filter) };
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
        bool assisted = false;
        if (startedGrounded && !move.InitiallyOverlapping
            && TryStep(world, state.Feet, horizontalVelocity * seconds, move, filter, Options.StepHeight) is { } stepped)
        {
            steppedUp = stepped.Y - state.Feet.Y;
            feet = stepped;
            move = move with { AcceptedDisplacement = stepped - state.Feet };
        }
        else if (!startedGrounded && Options.LedgeAssistHeight > 0 && vertical <= Options.LedgeAssistMaximumRise
            && !move.InitiallyOverlapping
            && TryStep(world, state.Feet, horizontalVelocity * seconds, move, filter, Options.LedgeAssistHeight) is { } ledge)
        {
            steppedUp = MathF.Max(0, ledge.Y - state.Feet.Y);
            feet = ledge;
            move = move with { AcceptedDisplacement = ledge - state.Feet };
            vertical = 0;
            assisted = true;
        }
        if (vertical > 0 && move.Contacts.Any(contact => contact.Normal.Y < -0.1f)) vertical = 0;
        // Sticky ground: a character that was walking follows stairs and small drops down instead of hopping off them.
        float snap = startedGrounded && vertical <= 0 ? MathF.Max(Options.GroundSnap, Options.StepDownHeight) : Options.GroundSnap;
        SpatialHit3D? landed = vertical <= 0 && !move.InitiallyOverlapping ? ProbeGround(world, feet, filter, snap) : null;
        grounded = landed is { Normal.Y: var landedY } && landedY >= groundNormalY;
        if (grounded)
        {
            SpatialHit3D contact = landed!.Value;
            Vector3 centre = feet + Vector3.UnitY * Options.Radius;
            // Snap down onto the support. Slopes keep a hair of clearance so float error never embeds the capsule; flat
            // ground snaps exactly onto its top, and Depenetrate tidies any residue at the start of the next step.
            float gap = Vector3.Dot(centre - contact.Point, contact.Normal) - Options.Radius - 0.0001f;
            feet -= Vector3.UnitY * MathF.Max(0, gap / contact.Normal.Y);
            if (contact.Normal.Y > 0.9999f) feet.Y = contact.Point.Y;
            vertical = 0;
        }
        SpatialHit3D? support = grounded ? landed : null;
        if (!grounded && startedGrounded && vertical <= 0 && Options.StepDownHeight > 0 && !move.InitiallyOverlapping)
        {
            // Stepping down off a stair: the capsule still rests on the upper corner while its axis is already over the
            // lower tread. Stay grounded (no hop, no landing) and let the corner ease it down over the next steps.
            SpatialHit3D? below = world.Raycast(new Ray3D(feet + Vector3.UnitY * Options.Radius, -Vector3.UnitY,
                Options.Radius + Options.StepDownHeight), filter);
            if (below is { } tread && tread.Normal.Y >= groundNormalY)
            {
                grounded = true;
                support = tread;
                vertical = 0;
            }
        }
        bool supported = grounded && world.Raycast(new Ray3D(feet + Vector3.UnitY * Options.Radius, -Vector3.UnitY,
            Options.Radius + Options.GroundSnap + 0.05f), filter) is not null;
        return new(new(feet, vertical), grounded, grounded ? support!.Value.Normal : Vector3.Zero,
            grounded ? support!.Value.ColliderId : null, move)
        {
            SteppedUp = steppedUp,
            CentreSupported = supported,
            LedgeAssisted = assisted,
        };
    }

    /// <summary>
    /// Up, forward, down. Taken only when a grounded move lost horizontal progress to a wall-like contact and
    /// walkable ground exists within <see cref="CharacterMotorOptions3D.StepHeight"/> above the feet.
    /// </summary>
    private Vector3? TryStep(ISpatialQueryWorld3D world, Vector3 feet, Vector3 requested, MoveResult3D plain,
        QueryFilter3D? filter, float step)
    {
        Vector3 horizontal = new(requested.X, 0, requested.Z);
        float length = horizontal.Length();
        if (step <= 0 || length < 1e-5f)
        {
            return null;
        }
        Vector3 direction = horizontal / length;
        float plainProgress = Vector3.Dot(new Vector3(plain.AcceptedDisplacement.X, 0, plain.AcceptedDisplacement.Z), direction);
        float facing = 0;
        Vector3 wall = Vector3.Zero;
        foreach (SpatialHit3D contact in plain.Contacts)
        {
            var flat = new Vector3(contact.Normal.X, 0, contact.Normal.Z);
            if (contact.Normal.Y < groundNormalY && flat.LengthSquared() > 1e-8f)
            {
                flat = Vector3.Normalize(flat);
                float candidateFacing = -Vector3.Dot(flat, direction);
                if (candidateFacing > facing)
                {
                    facing = candidateFacing;
                    wall = flat;
                }
            }
        }
        if (facing < 0.1f || plainProgress >= length - 1e-4f)
        {
            return null;
        }
        // The capsule's lower sphere must travel far enough past the ledge's corner that the corner presents a walkable
        // normal. That distance is measured into the ledge, so an oblique approach needs proportionally more travel
        // along its own direction. Slow or angled approaches advance by that minimum instead of stalling.
        float intoLedge = Options.Radius * (1 - MathF.Sin(Options.MaximumSlopeDegrees * MathF.PI / 180)) + 0.005f;
        // Start from where the ordinary move stopped against the ledge, so the advance is measured from contact.
        Vector3 start = feet + plain.AcceptedDisplacement;
        // Continue along the requested direction, plus just enough straight into the ledge for the corner to be
        // walkable. Measuring that part along the wall normal keeps glancing approaches from popping sideways.
        Vector3 travel = direction * MathF.Max(0, length - plainProgress);
        float shortfall = intoLedge - MathF.Max(0, Vector3.Dot(travel, -wall));
        if (shortfall > 0)
        {
            travel += -wall * shortfall;
        }
        Capsule3D capsule = Capsule3D.AtFeet(start, Options.Radius, Options.Height);
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
        MoveResult3D across = world.SweepAndSlide(raised, travel, filter, minimumGroundNormalY: groundNormalY);
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
        Vector3 candidate = start + Vector3.UnitY * rise + across.AcceptedDisplacement
            - Vector3.UnitY * MathF.Max(0, landing.TimeOfImpact * drop - 0.0005f);
        // Height is measured from where the ordinary move left the feet (for an airborne assist, after this step's fall).
        float gained = candidate.Y - start.Y;
        float progress = Vector3.Dot(new Vector3(candidate.X - feet.X, 0, candidate.Z - feet.Z), direction);
        if (gained <= 1e-4f || gained > step + 1e-4f || progress <= plainProgress + 1e-4f)
        {
            return null;
        }
        // Resting on a ledge corner leaves the feet slightly below its top. The character must also fit when standing
        // on the top itself, otherwise a low ceiling would wedge it on the corner.
        Vector3 standing = candidate with { Y = MathF.Max(candidate.Y, landing.Point.Y) };
        if (standing.Y - start.Y > step + 1e-4f
            || !world.Overlap(Capsule3D.AtFeet(candidate, Options.Radius, Options.Height), filter).IsEmpty
            || !world.Overlap(Capsule3D.AtFeet(standing, Options.Radius, Options.Height), filter).IsEmpty)
        {
            return null;
        }
        return candidate;
    }

    /// <summary>Largest overlap the motor resolves by itself; deeper overlaps are reported as embedded (for example, crushes).</summary>
    public const float MaximumCorrection = 0.05f;

    /// <summary>
    /// Push the capsule out of shallow overlaps left by floating-point error at seams and ledges. Each overlap moves the
    /// capsule along its contact normal by its penetration depth; overlaps deeper than <see cref="MaximumCorrection"/>
    /// are left alone so genuine embedding stays visible to the caller.
    /// </summary>
    public Vector3 Depenetrate(ISpatialQueryWorld3D world, Vector3 feet, QueryFilter3D? filter = null)
    {
        for (int iteration = 0; iteration < 4; iteration++)
        {
            Capsule3D capsule = Capsule3D.AtFeet(feet, Options.Radius, Options.Height);
            var overlaps = world.Overlap(capsule, filter);
            if (overlaps.IsEmpty)
            {
                return feet;
            }
            Vector3 push = Vector3.Zero;
            foreach (SpatialHit3D hit in overlaps)
            {
                Vector3 axis = capsule.B - capsule.A;
                float along = axis.LengthSquared() > 1e-12f
                    ? Math.Clamp(Vector3.Dot(hit.Point - capsule.A, axis) / axis.LengthSquared(), 0, 1)
                    : 0;
                Vector3 closest = capsule.A + axis * along;
                Vector3 offset = closest - hit.Point;
                float distance = offset.Length();
                float depth = Options.Radius - distance;
                if (distance < 1e-6f || depth > MaximumCorrection)
                {
                    return feet;
                }
                push += offset / distance * (depth + 0.0002f);
            }
            feet += push;
        }
        return feet;
    }

    public SpatialHit3D? ProbeGround(ISpatialQueryWorld3D world, Vector3 feet, QueryFilter3D? filter = null)
        => ProbeGround(world, feet, filter, Options.GroundSnap);

    private SpatialHit3D? ProbeGround(ISpatialQueryWorld3D world, Vector3 feet, QueryFilter3D? filter, float distance)
    {
        ArgumentNullException.ThrowIfNull(world);
        SpatialMath3D.RequireFinite(feet);
        Capsule3D raised = Capsule3D.AtFeet(feet + Vector3.UnitY * 0.001f, Options.Radius, Options.Height);
        return world.Sweep(raised, -Vector3.UnitY * (distance + 0.001f), filter);
    }
}
