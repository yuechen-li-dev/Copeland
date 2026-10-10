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
                return new(displacement, accepted, contacts.ToImmutable(), true, iterations + 1);
            }
            float travel = MathF.Max(0, hit.Value.TimeOfImpact - .00001f / remaining.Length());
            accepted += remaining * travel;
            remaining *= 1 - travel;
            Vector3 normal = hit.Value.Normal;
            if (normal.Y > 0 && normal.Y < minimumGroundNormalY)
            {
                normal = Vector3.Normalize(new Vector3(normal.X, 0, normal.Z));
            }
            float into = Vector3.Dot(remaining, normal);
            if (into < 0)
            {
                remaining -= normal * into;
            }
            if (hit.Value.Status == SpatialQueryStatus3D.IterationLimit)
            {
                break;
            }
        }
        return new(displacement, accepted, contacts.ToImmutable(), false, iterations + 1);
    }
}
