namespace Aurelian.Spatial2D;

internal static class SpatialGeometry2D
{
    public static bool Overlaps(SpatialShape2D left, SpatialShape2D right)
    {
        left.Validate();
        right.Validate();
        return (left, right) switch
        {
            (Aabb2 a, Aabb2 b) => Overlaps(a, b),
            (Circle2 a, Circle2 b) => Overlaps(a, b),
            (Circle2 circle, Aabb2 box) => Overlaps(circle, box),
            (Aabb2 box, Circle2 circle) => Overlaps(circle, box),
            _ => throw new NotSupportedException($"Unsupported shape pair {left.GetType().Name}/{right.GetType().Name}.")
        };
    }

    public static bool Contains(SpatialShape2D shape, SpatialPoint2D point)
    {
        shape.Validate();
        point.Validate();
        return shape switch
        {
            Aabb2 box => point.X >= box.MinX && point.X <= box.MaxX
                && point.Y >= box.MinY && point.Y <= box.MaxY,
            Circle2 circle => (point - circle.Center).LengthSquared <= circle.Radius * circle.Radius,
            _ => throw new NotSupportedException($"Unsupported shape {shape.GetType().Name}.")
        };
    }

    public static SpatialHit2D? Sweep(
        SpatialShape2D moving,
        SpatialVector2D displacement,
        SpatialCollider2D collider)
    {
        moving.Validate();
        displacement.Validate();
        collider.Shape.Validate();
        if (OverlapsStrict(moving, collider.Shape))
        {
            SpatialVector2D normal = PenetrationNormal(moving.Center, collider.Shape.Center);
            return new SpatialHit2D(
                collider.Id,
                0,
                normal,
                moving.Center,
                true,
                collider.SemanticOwnerId);
        }
        if (displacement.LengthSquared <= SpatialMath2D.Epsilon * SpatialMath2D.Epsilon)
        {
            return null;
        }

        RayHit? rayHit = (moving, collider.Shape) switch
        {
            (Aabb2 a, Aabb2 b) => RayAabb(
                a.Center,
                displacement,
                b.MinX - a.HalfExtents.X,
                b.MaxX + a.HalfExtents.X,
                b.MinY - a.HalfExtents.Y,
                b.MaxY + a.HalfExtents.Y),
            (Circle2 a, Circle2 b) => RayCircle(a.Center, displacement, b.Center, a.Radius + b.Radius),
            (Circle2 circle, Aabb2 box) => RayRoundedAabb(
                circle.Center,
                displacement,
                box,
                circle.Radius),
            (Aabb2 box, Circle2 circle) => Reverse(
                RayRoundedAabb(circle.Center, displacement * -1, box, circle.Radius)),
            _ => throw new NotSupportedException(
                $"Unsupported sweep pair {moving.GetType().Name}/{collider.Shape.GetType().Name}.")
        };
        if (rayHit is null)
        {
            return null;
        }
        RayHit resolved = rayHit.Value;
        if (resolved.Time < -SpatialMath2D.Epsilon
            || resolved.Time > 1 + SpatialMath2D.Epsilon
            || (resolved.Time <= SpatialMath2D.Epsilon
                && SpatialVector2D.Dot(displacement, resolved.Normal) >= -SpatialMath2D.Epsilon))
        {
            return null;
        }

        double time = Math.Clamp(resolved.Time, 0, 1);
        SpatialPoint2D centerAtHit = moving.Center + (displacement * time);
        SpatialPoint2D contact = ContactPoint(moving, centerAtHit, resolved.Normal);
        return new SpatialHit2D(
            collider.Id,
            time,
            resolved.Normal,
            contact,
            false,
            collider.SemanticOwnerId);
    }

    private static bool Overlaps(Aabb2 left, Aabb2 right)
    {
        return left.MinX <= right.MaxX && left.MaxX >= right.MinX
            && left.MinY <= right.MaxY && left.MaxY >= right.MinY;
    }

    private static bool Overlaps(Circle2 left, Circle2 right)
    {
        double radii = left.Radius + right.Radius;
        return (left.Center - right.Center).LengthSquared <= radii * radii;
    }

    private static bool Overlaps(Circle2 circle, Aabb2 box)
    {
        double x = Math.Clamp(circle.Center.X, box.MinX, box.MaxX);
        double y = Math.Clamp(circle.Center.Y, box.MinY, box.MaxY);
        double deltaX = circle.Center.X - x;
        double deltaY = circle.Center.Y - y;
        return (deltaX * deltaX) + (deltaY * deltaY) <= circle.Radius * circle.Radius;
    }

    private static bool OverlapsStrict(SpatialShape2D left, SpatialShape2D right)
    {
        return (left, right) switch
        {
            (Aabb2 a, Aabb2 b) => a.MinX < b.MaxX - SpatialMath2D.Epsilon
                && a.MaxX > b.MinX + SpatialMath2D.Epsilon
                && a.MinY < b.MaxY - SpatialMath2D.Epsilon
                && a.MaxY > b.MinY + SpatialMath2D.Epsilon,
            (Circle2 a, Circle2 b) => (a.Center - b.Center).LengthSquared
                < Math.Max(0, (a.Radius + b.Radius) * (a.Radius + b.Radius) - SpatialMath2D.Epsilon),
            (Circle2 circle, Aabb2 box) => CircleAabbStrict(circle, box),
            (Aabb2 box, Circle2 circle) => CircleAabbStrict(circle, box),
            _ => false
        };
    }

    private static bool CircleAabbStrict(Circle2 circle, Aabb2 box)
    {
        double x = Math.Clamp(circle.Center.X, box.MinX, box.MaxX);
        double y = Math.Clamp(circle.Center.Y, box.MinY, box.MaxY);
        double deltaX = circle.Center.X - x;
        double deltaY = circle.Center.Y - y;
        if (circle.Radius == 0)
        {
            return circle.Center.X > box.MinX + SpatialMath2D.Epsilon
                && circle.Center.X < box.MaxX - SpatialMath2D.Epsilon
                && circle.Center.Y > box.MinY + SpatialMath2D.Epsilon
                && circle.Center.Y < box.MaxY - SpatialMath2D.Epsilon;
        }
        return (deltaX * deltaX) + (deltaY * deltaY)
            < (circle.Radius * circle.Radius) - SpatialMath2D.Epsilon;
    }

    private static RayHit? RayAabb(
        SpatialPoint2D origin,
        SpatialVector2D delta,
        double minimumX,
        double maximumX,
        double minimumY,
        double maximumY)
    {
        double entry = 0;
        double exit = 1;
        SpatialVector2D normal = SpatialVector2D.Zero;
        if (!ClipAxis(origin.X, delta.X, minimumX, maximumX, new SpatialVector2D(-1, 0), new SpatialVector2D(1, 0), ref entry, ref exit, ref normal))
        {
            return null;
        }
        if (!ClipAxis(origin.Y, delta.Y, minimumY, maximumY, new SpatialVector2D(0, -1), new SpatialVector2D(0, 1), ref entry, ref exit, ref normal))
        {
            return null;
        }
        return new RayHit(entry, normal);
    }

    private static bool ClipAxis(
        double origin,
        double delta,
        double minimum,
        double maximum,
        SpatialVector2D minimumNormal,
        SpatialVector2D maximumNormal,
        ref double entry,
        ref double exit,
        ref SpatialVector2D normal)
    {
        if (Math.Abs(delta) <= SpatialMath2D.Epsilon)
        {
            return origin >= minimum - SpatialMath2D.Epsilon
                && origin <= maximum + SpatialMath2D.Epsilon;
        }
        double first = (minimum - origin) / delta;
        double second = (maximum - origin) / delta;
        SpatialVector2D firstNormal = minimumNormal;
        if (first > second)
        {
            (first, second) = (second, first);
            firstNormal = maximumNormal;
        }
        if (first > entry + SpatialMath2D.Epsilon)
        {
            entry = first;
            normal = firstNormal;
        }
        else if (Math.Abs(first - entry) <= SpatialMath2D.Epsilon
            && CompareNormal(firstNormal, normal) < 0)
        {
            normal = firstNormal;
        }
        exit = Math.Min(exit, second);
        return entry <= exit + SpatialMath2D.Epsilon;
    }

    private static RayHit? RayCircle(
        SpatialPoint2D origin,
        SpatialVector2D delta,
        SpatialPoint2D center,
        double radius)
    {
        SpatialVector2D offset = origin - center;
        double a = delta.LengthSquared;
        double b = 2 * SpatialVector2D.Dot(offset, delta);
        double c = offset.LengthSquared - (radius * radius);
        double discriminant = (b * b) - (4 * a * c);
        if (discriminant < -SpatialMath2D.Epsilon)
        {
            return null;
        }
        double time = (-b - Math.Sqrt(Math.Max(0, discriminant))) / (2 * a);
        SpatialPoint2D impact = origin + (delta * time);
        return new RayHit(time, Normalize(impact - center, delta * -1));
    }

    private static RayHit? RayRoundedAabb(
        SpatialPoint2D origin,
        SpatialVector2D delta,
        Aabb2 box,
        double radius)
    {
        RayHit? broad = RayAabb(
            origin,
            delta,
            box.MinX - radius,
            box.MaxX + radius,
            box.MinY - radius,
            box.MaxY + radius);
        if (broad is null)
        {
            return null;
        }

        RayHit? best = null;
        ConsiderFace(ref best, origin, delta, box.MinX - radius, true, box.MinY, box.MaxY, new SpatialVector2D(-1, 0));
        ConsiderFace(ref best, origin, delta, box.MaxX + radius, true, box.MinY, box.MaxY, new SpatialVector2D(1, 0));
        ConsiderFace(ref best, origin, delta, box.MinY - radius, false, box.MinX, box.MaxX, new SpatialVector2D(0, -1));
        ConsiderFace(ref best, origin, delta, box.MaxY + radius, false, box.MinX, box.MaxX, new SpatialVector2D(0, 1));
        if (radius > 0)
        {
            ConsiderCorner(ref best, origin, delta, new SpatialPoint2D(box.MinX, box.MinY), box.Center, radius);
            ConsiderCorner(ref best, origin, delta, new SpatialPoint2D(box.MaxX, box.MinY), box.Center, radius);
            ConsiderCorner(ref best, origin, delta, new SpatialPoint2D(box.MinX, box.MaxY), box.Center, radius);
            ConsiderCorner(ref best, origin, delta, new SpatialPoint2D(box.MaxX, box.MaxY), box.Center, radius);
        }
        return best;
    }

    private static void ConsiderFace(
        ref RayHit? best,
        SpatialPoint2D origin,
        SpatialVector2D delta,
        double plane,
        bool vertical,
        double rangeMinimum,
        double rangeMaximum,
        SpatialVector2D normal)
    {
        double component = vertical ? delta.X : delta.Y;
        if (Math.Abs(component) <= SpatialMath2D.Epsilon)
        {
            return;
        }
        double start = vertical ? origin.X : origin.Y;
        double time = (plane - start) / component;
        SpatialPoint2D point = origin + (delta * time);
        double tangent = vertical ? point.Y : point.X;
        if (tangent >= rangeMinimum - SpatialMath2D.Epsilon
            && tangent <= rangeMaximum + SpatialMath2D.Epsilon)
        {
            Consider(ref best, new RayHit(time, normal));
        }
    }

    private static void ConsiderCorner(
        ref RayHit? best,
        SpatialPoint2D origin,
        SpatialVector2D delta,
        SpatialPoint2D corner,
        SpatialPoint2D center,
        double radius)
    {
        RayHit? hit = RayCircle(origin, delta, corner, radius);
        if (hit is not null && InCornerQuadrant(origin + (delta * hit.Value.Time), corner, center))
        {
            Consider(ref best, hit.Value);
        }
    }

    private static void Consider(ref RayHit? best, RayHit candidate)
    {
        if (candidate.Time < -SpatialMath2D.Epsilon || candidate.Time > 1 + SpatialMath2D.Epsilon)
        {
            return;
        }
        if (best is null
            || candidate.Time < best.Value.Time - SpatialMath2D.Epsilon
            || Math.Abs(candidate.Time - best.Value.Time) <= SpatialMath2D.Epsilon
                && NormalKey(candidate.Normal) < NormalKey(best.Value.Normal))
        {
            best = candidate;
        }
    }

    private static bool InCornerQuadrant(SpatialPoint2D point, SpatialPoint2D corner, SpatialPoint2D center)
    {
        bool xMatches = corner.X < center.X
            ? point.X <= corner.X + SpatialMath2D.Epsilon
            : point.X >= corner.X - SpatialMath2D.Epsilon;
        bool yMatches = corner.Y < center.Y
            ? point.Y <= corner.Y + SpatialMath2D.Epsilon
            : point.Y >= corner.Y - SpatialMath2D.Epsilon;
        return xMatches && yMatches;
    }

    private static RayHit? Reverse(RayHit? hit)
    {
        return hit is null ? null : new RayHit(hit.Value.Time, hit.Value.Normal * -1);
    }

    private static SpatialVector2D Normalize(SpatialVector2D value, SpatialVector2D fallback)
    {
        double length = Math.Sqrt(value.LengthSquared);
        if (length <= SpatialMath2D.Epsilon)
        {
            double fallbackLength = Math.Sqrt(fallback.LengthSquared);
            return fallbackLength <= SpatialMath2D.Epsilon
                ? new SpatialVector2D(-1, 0)
                : fallback * (1 / fallbackLength);
        }
        return value * (1 / length);
    }

    private static SpatialVector2D PenetrationNormal(SpatialPoint2D moving, SpatialPoint2D obstacle)
    {
        SpatialVector2D delta = moving - obstacle;
        if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
        {
            return new SpatialVector2D(delta.X < 0 ? -1 : 1, 0);
        }
        return new SpatialVector2D(0, delta.Y < 0 ? -1 : 1);
    }

    private static SpatialPoint2D ContactPoint(
        SpatialShape2D moving,
        SpatialPoint2D centerAtHit,
        SpatialVector2D normal)
    {
        return moving switch
        {
            Circle2 circle => centerAtHit + (normal * -circle.Radius),
            Aabb2 box => centerAtHit + new SpatialVector2D(
                -normal.X * box.HalfExtents.X,
                -normal.Y * box.HalfExtents.Y),
            _ => centerAtHit
        };
    }

    private static int CompareNormal(SpatialVector2D left, SpatialVector2D right)
    {
        return NormalKey(left).CompareTo(NormalKey(right));
    }

    private static int NormalKey(SpatialVector2D normal)
    {
        if (normal.X < -SpatialMath2D.Epsilon) return 0;
        if (normal.Y < -SpatialMath2D.Epsilon) return 1;
        if (normal.X > SpatialMath2D.Epsilon) return 2;
        if (normal.Y > SpatialMath2D.Epsilon) return 3;
        return 4;
    }

    private readonly record struct RayHit(double Time, SpatialVector2D Normal);
}
