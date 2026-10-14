using System.Numerics;

namespace Aurelian.Spatial3D;

/// <summary>
/// A third-person camera arm that never passes through geometry. A sphere is swept from the pivot (usually the
/// character's head) toward the desired eye; when something is in the way the arm shortens immediately, and when the
/// obstruction clears it eases back out so the camera does not pop. The caller owns the pivot, direction and timing.
/// </summary>
public sealed class CameraBoom3D
{
    public CameraBoom3D(float radius = 0.2f, float margin = 0.05f, float extendRate = 6)
    {
        if (!float.IsFinite(radius) || radius <= 0 || !float.IsFinite(margin) || margin < 0
            || !float.IsFinite(extendRate) || extendRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Camera booms need a positive radius and extend rate.");
        }
        Radius = radius;
        Margin = margin;
        ExtendRate = extendRate;
    }

    public float Radius { get; }
    public float Margin { get; }

    /// <summary>Metres per second the arm regains after an obstruction clears.</summary>
    public float ExtendRate { get; }

    /// <summary>Current arm length; negative until the first update.</summary>
    public float Length { get; private set; } = -1;

    /// <summary>The farthest unobstructed arm length along <paramref name="direction"/>, without smoothing.</summary>
    public float Clearance(ISpatialQueryWorld3D world, Vector3 pivot, Vector3 direction, float desiredLength,
        QueryFilter3D? filter = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        SpatialMath3D.RequireFinite(pivot);
        SpatialMath3D.RequireFinite(direction);
        if (!float.IsFinite(desiredLength) || desiredLength < 0 || MathF.Abs(direction.LengthSquared() - 1) > 1e-3f)
        {
            throw new ArgumentException("Camera arms need a unit direction and a finite nonnegative length.");
        }
        if (desiredLength <= 1e-5f)
        {
            return 0;
        }
        SpatialHit3D? hit = world.Sweep(Capsule3D.Sphere(pivot, Radius), direction * desiredLength, filter);
        if (hit is not { } contact)
        {
            return desiredLength;
        }
        if (contact.Status == SpatialQueryStatus3D.InitiallyOverlapping)
        {
            return 0;
        }
        return Math.Clamp(contact.TimeOfImpact * desiredLength - Margin, 0, desiredLength);
    }

    /// <summary>Advance the arm and return the eye position.</summary>
    public Vector3 Update(ISpatialQueryWorld3D world, Vector3 pivot, Vector3 direction, float desiredLength, float seconds,
        QueryFilter3D? filter = null)
    {
        if (!float.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        float clear = Clearance(world, pivot, direction, desiredLength, filter);
        if (Length < 0 || clear < Length)
        {
            Length = clear;
        }
        else
        {
            Length = MathF.Min(clear, Length + ExtendRate * seconds);
        }
        return pivot + direction * Length;
    }

    /// <summary>Forget smoothing (for example after a respawn or camera cut).</summary>
    public void Reset() => Length = -1;
}
