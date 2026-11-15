using System.Numerics;

namespace Aurelian.Spatial3D;

/// <summary>Presentation clearance against the same authored colliders used by movement. No simulation mutation.</summary>
public static class FirstPersonWeaponProbe3D
{
    public static float Measure(ISpatialQueryWorld3D world, Vector3 eye, Vector3 direction, float aim = 0,
        float reach = 1.05f, float radius = .085f, QueryFilter3D? filter = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        new Ray3D(eye, direction, reach).Validate();
        if (!float.IsFinite(aim) || aim < 0 || aim > 1 || !float.IsFinite(radius) || radius <= 0 || radius > .25f
            || reach > 2 || MathF.Abs(direction.Y) > .99f)
        {
            throw new ArgumentOutOfRangeException(nameof(aim));
        }
        Vector3 right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, direction));
        Vector3 offset = right * (.24f * (1 - aim)) + up * (-.22f + .15f * aim);
        var origin = Capsule3D.Sphere(eye, radius);
        // The lateral path catches side walls containing the hip-fire origin, including initial overlap.
        if (world.Sweep(origin, offset, filter) is not null)
        {
            return 0;
        }
        var hit = world.Sweep(Capsule3D.Sphere(eye + offset, radius), direction * reach, filter);
        return hit is null ? reach : Math.Clamp(hit.Value.Distance, 0, reach);
    }
}
