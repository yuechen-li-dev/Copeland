namespace Aurelian.Spatial2D;

public static class SpatialMath2D
{
    public const double Epsilon = 1e-9;

    internal static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Spatial values must be finite.");
        }
    }
}

public readonly record struct SpatialPoint2D(double X, double Y)
{
    public void Validate()
    {
        SpatialMath2D.RequireFinite(X, nameof(X));
        SpatialMath2D.RequireFinite(Y, nameof(Y));
    }

    public static SpatialVector2D operator -(SpatialPoint2D left, SpatialPoint2D right)
    {
        return new SpatialVector2D(left.X - right.X, left.Y - right.Y);
    }

    public static SpatialPoint2D operator +(SpatialPoint2D point, SpatialVector2D vector)
    {
        return new SpatialPoint2D(point.X + vector.X, point.Y + vector.Y);
    }
}

public readonly record struct SpatialVector2D(double X, double Y)
{
    public static SpatialVector2D Zero => new(0, 0);

    public double LengthSquared => (X * X) + (Y * Y);

    public void Validate()
    {
        SpatialMath2D.RequireFinite(X, nameof(X));
        SpatialMath2D.RequireFinite(Y, nameof(Y));
    }

    public static SpatialVector2D operator +(SpatialVector2D left, SpatialVector2D right)
    {
        return new SpatialVector2D(left.X + right.X, left.Y + right.Y);
    }

    public static SpatialVector2D operator -(SpatialVector2D left, SpatialVector2D right)
    {
        return new SpatialVector2D(left.X - right.X, left.Y - right.Y);
    }

    public static SpatialVector2D operator *(SpatialVector2D value, double scale)
    {
        return new SpatialVector2D(value.X * scale, value.Y * scale);
    }

    public static double Dot(SpatialVector2D left, SpatialVector2D right)
    {
        return (left.X * right.X) + (left.Y * right.Y);
    }
}

public abstract record SpatialShape2D
{
    public abstract SpatialPoint2D Center { get; init; }

    public abstract void Validate();

    public abstract SpatialShape2D Translate(SpatialVector2D displacement);
}

public sealed record Aabb2(SpatialPoint2D Center, SpatialVector2D HalfExtents) : SpatialShape2D
{
    public double MinX => Center.X - HalfExtents.X;
    public double MaxX => Center.X + HalfExtents.X;
    public double MinY => Center.Y - HalfExtents.Y;
    public double MaxY => Center.Y + HalfExtents.Y;

    public override void Validate()
    {
        Center.Validate();
        HalfExtents.Validate();
        if (HalfExtents.X < 0 || HalfExtents.Y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HalfExtents), "AABB half extents must not be negative.");
        }
    }

    public override SpatialShape2D Translate(SpatialVector2D displacement)
    {
        return this with { Center = Center + displacement };
    }
}

public sealed record Circle2(SpatialPoint2D Center, double Radius) : SpatialShape2D
{
    public override void Validate()
    {
        Center.Validate();
        SpatialMath2D.RequireFinite(Radius, nameof(Radius));
        if (Radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Radius), "Circle radius must not be negative.");
        }
    }

    public override SpatialShape2D Translate(SpatialVector2D displacement)
    {
        return this with { Center = Center + displacement };
    }
}

public readonly record struct SpatialColliderId(string Value) : IComparable<SpatialColliderId>
{
    public int CompareTo(SpatialColliderId other)
    {
        return StringComparer.Ordinal.Compare(Value, other.Value);
    }

    public override string ToString() => Value;
}

public readonly record struct SpatialTriggerId(string Value) : IComparable<SpatialTriggerId>
{
    public int CompareTo(SpatialTriggerId other)
    {
        return StringComparer.Ordinal.Compare(Value, other.Value);
    }

    public override string ToString() => Value;
}

[Flags]
public enum SpatialLayerMask : uint
{
    None = 0,
    Layer0 = 1u << 0,
    Layer1 = 1u << 1,
    Layer2 = 1u << 2,
    Layer3 = 1u << 3,
    All = uint.MaxValue
}

public readonly record struct SpatialQueryFilter(
    SpatialLayerMask IncludedLayers,
    SpatialLayerMask QueryLayer = SpatialLayerMask.All)
{
    public static SpatialQueryFilter All => new(SpatialLayerMask.All);
}

public sealed record SpatialCollider2D(
    SpatialColliderId Id,
    SpatialShape2D Shape,
    SpatialLayerMask Layer = SpatialLayerMask.Layer0,
    SpatialLayerMask Mask = SpatialLayerMask.All,
    string? SemanticOwnerId = null);

public sealed record SpatialTrigger2D(
    SpatialTriggerId Id,
    SpatialShape2D Shape,
    SpatialLayerMask Layer = SpatialLayerMask.Layer0,
    SpatialLayerMask Mask = SpatialLayerMask.All,
    string? SemanticOwnerId = null);

public readonly record struct SpatialOverlap2D(
    SpatialColliderId ColliderId,
    double SquaredCenterDistance,
    string? SemanticOwnerId);

public readonly record struct SpatialTriggerOverlap2D(
    SpatialTriggerId TriggerId,
    double SquaredCenterDistance,
    string? SemanticOwnerId);

public readonly record struct SpatialHit2D(
    SpatialColliderId ColliderId,
    double TimeOfImpact,
    SpatialVector2D Normal,
    SpatialPoint2D ContactPoint,
    bool InitiallyOverlapping,
    string? SemanticOwnerId);

public sealed record SpatialMoveResult(
    SpatialVector2D RequestedDisplacement,
    SpatialVector2D AcceptedDisplacement,
    IReadOnlyList<SpatialHit2D> Contacts,
    bool StartedOverlapping,
    int Iterations);

public sealed record TriggerTransition2D(
    IReadOnlyList<SpatialTriggerId> Entered,
    IReadOnlyList<SpatialTriggerId> Stayed,
    IReadOnlyList<SpatialTriggerId> Exited);

public enum SpatialDebugFactKind
{
    Solid,
    Trigger,
    Sweep,
    Contact
}

public sealed record SpatialDebugFact2D(
    SpatialDebugFactKind Kind,
    string StableId,
    SpatialShape2D? Shape = null,
    SpatialPoint2D? Start = null,
    SpatialPoint2D? End = null,
    SpatialPoint2D? ContactPoint = null,
    SpatialVector2D? Normal = null);
