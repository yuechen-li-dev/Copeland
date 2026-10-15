using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Spatial3D;

/// <summary>Shared movement policy over query facts. The query owner decides geometry and contact identity.</summary>
public static class SpatialMovement3D
{
    public static MoveResult3D SweepAndSlide(ISpatialQueryWorld3D world, Capsule3D shape, Vector3 displacement,
        QueryFilter3D? filter = null, int maximumIterations = 6, float minimumGroundNormalY = 0)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (maximumIterations is < 1 or > 16 || !float.IsFinite(minimumGroundNormalY)
            || minimumGroundNormalY < 0 || minimumGroundNormalY > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumIterations));
        }
        shape.Validate();
        SpatialMath3D.RequireFinite(displacement);
        Vector3 accepted = Vector3.Zero;
        Vector3 remaining = displacement;
        var contacts = ImmutableArray.CreateBuilder<SpatialHit3D>();
        int iterations = 0;
        for (; iterations < maximumIterations && remaining.LengthSquared() > 1e-12f; iterations++)
        {
            SpatialHit3D? hit = world.Sweep(shape.Translated(accepted), remaining, filter);
            if (hit is null)
            {
                accepted += remaining;
                break;
            }
            contacts.Add(hit.Value);
            if (hit.Value.Status == SpatialQueryStatus3D.InitiallyOverlapping)
            {
                // An overlap at the start is real embedding. After sliding, it is float error at the contact we just
                // stopped against (seams, ledge corners): keep what was accepted and let the caller's depenetration
                // tidy it, rather than reporting the mover as embedded.
                return new(displacement, accepted, contacts.ToImmutable(), iterations == 0, iterations + 1);
            }
            float travel = MathF.Max(0, hit.Value.TimeOfImpact - .00001f / remaining.Length());
            accepted += remaining * travel;
            remaining *= 1 - travel;
            remaining = Slide(remaining, hit.Value.Normal, minimumGroundNormalY);
            if (hit.Value.Status == SpatialQueryStatus3D.IterationLimit)
            {
                break;
            }
        }
        return new(displacement, accepted, contacts.ToImmutable(), false, iterations + 1);
    }

    /// <summary>
    /// Remove the part of <paramref name="remaining"/> that pushes into a contact. A surface too steep to stand on must not
    /// lift the mover (it acts as a wall), but it must still let the mover slide down and off it; flattening the normal
    /// unconditionally would cancel a fall onto a ledge corner and leave the mover hovering there.
    /// </summary>
    public static Vector3 Slide(Vector3 remaining, Vector3 normal, float minimumGroundNormalY)
    {
        float into = Vector3.Dot(remaining, normal);
        if (into >= 0)
        {
            return remaining;
        }
        Vector3 projected = remaining - normal * into;
        bool steep = normal.Y > 0 && normal.Y < minimumGroundNormalY;
        if (!steep || projected.Y <= MathF.Max(remaining.Y, 0) + 1e-6f)
        {
            return projected;
        }
        var flat = new Vector3(normal.X, 0, normal.Z);
        if (flat.LengthSquared() < 1e-12f)
        {
            return projected;
        }
        flat = Vector3.Normalize(flat);
        float horizontalInto = Vector3.Dot(remaining, flat);
        return horizontalInto < 0 ? remaining - flat * horizontalInto : remaining;
    }
}
