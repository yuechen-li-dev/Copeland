using Aurelian.Spatial2D;

namespace Aurelian.Combat;

public readonly record struct CombatMoveId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct CombatActorId(string Value) : IComparable<CombatActorId>
{
    public int CompareTo(CombatActorId other)
    {
        return StringComparer.Ordinal.Compare(Value, other.Value);
    }

    public override string ToString() => Value;
}

public enum CombatPhase
{
    Startup,
    Active,
    Recovery,
    Complete
}

public enum CombatAttackSide
{
    Left = -1,
    Right = 1
}

public abstract record AttackShape;

public sealed record PointAttackShape(double Radius) : AttackShape;

public sealed record LineAttackShape(double Length, double Width) : AttackShape;

public sealed record ArcAttackShape(double Radius, double ArcRadians) : AttackShape;

public sealed record RingAttackShape(double Radius, double Width) : AttackShape;

public sealed record CombatMoveDefinition(
    CombatMoveId Id,
    double Damage,
    AttackShape Shape,
    int StartupTicks,
    int ActiveTicks,
    int RecoveryTicks,
    double Knockback,
    double Lunge = 0,
    bool MayHitTargetMoreThanOnce = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id.Value))
        {
            throw new ArgumentException("Combat move id is required.", nameof(Id));
        }
        if (!double.IsFinite(Damage) || Damage < 0
            || !double.IsFinite(Knockback) || Knockback < 0
            || !double.IsFinite(Lunge))
        {
            throw new ArgumentOutOfRangeException(nameof(Damage), "Move tuning must be finite and non-negative, except lunge may be signed.");
        }
        if (StartupTicks < 1 || ActiveTicks < 1 || RecoveryTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(StartupTicks), "Every combat phase must contain at least one tick.");
        }
        CombatGeometry.Validate(Shape);
    }
}

public sealed record CombatActionState(
    long ActionId,
    CombatMoveId Move,
    CombatActorId Source,
    SpatialPoint2D Origin,
    double FacingRadians,
    CombatAttackSide Side,
    CombatPhase Phase,
    int PhaseTick,
    IReadOnlyList<CombatActorId> HitTargets,
    bool ContactTriggered)
{
    public static CombatActionState Start(
        long actionId,
        CombatMoveDefinition move,
        CombatActorId source,
        SpatialPoint2D origin,
        double facingRadians,
        CombatAttackSide side = CombatAttackSide.Right)
    {
        move.Validate();
        origin.Validate();
        if (!double.IsFinite(facingRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(facingRadians));
        }
        return new CombatActionState(
            actionId,
            move.Id,
            source,
            origin,
            facingRadians,
            side,
            CombatPhase.Startup,
            0,
            [],
            false);
    }
}

public sealed record CombatTarget(CombatActorId Id, SpatialPoint2D Position, double Radius = 0);

public sealed record CombatContact(
    long ActionId,
    CombatMoveId Move,
    CombatActorId Source,
    CombatActorId Target,
    double Damage,
    SpatialPoint2D Point,
    SpatialVector2D Impulse);

public sealed record CombatPresentationEvent(
    long ActionId,
    CombatMoveId Move,
    CombatPhase Phase,
    int PhaseTick,
    bool ContactTriggered);

public sealed record CombatStepResult(
    CombatActionState State,
    IReadOnlyList<CombatContact> Contacts,
    CombatPresentationEvent Presentation,
    string? PhaseTransitionRuleId);
