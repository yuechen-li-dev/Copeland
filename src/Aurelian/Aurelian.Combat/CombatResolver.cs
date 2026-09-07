using Aurelian.Spatial2D;
using Dominatus.Core.Transitions;
using Dominatus.OptFlow;

namespace Aurelian.Combat;

public static class CombatResolver
{
    private static readonly TransitionDefinition<PhaseState, PhaseEvent, PhaseContext, PhaseEffect> PhaseTransitions
        = DefinePhaseTransitions();

    public static CombatStepResult Advance(
        CombatMoveDefinition move,
        CombatActionState state,
        IEnumerable<CombatTarget> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        move.Validate();
        if (move.Id != state.Move)
        {
            throw new ArgumentException("The action and move definition do not match.", nameof(move));
        }

        IReadOnlyList<CombatActorId> hitTargets = state.HitTargets;
        IReadOnlyList<CombatContact> contacts = [];
        if (state.Phase == CombatPhase.Active)
        {
            var mutableHitTargets = state.HitTargets.ToHashSet();
            var mutableContacts = new List<CombatContact>();
            foreach (CombatTarget target in candidates.OrderBy(candidate => candidate.Id))
            {
                if (target.Id == state.Source
                    || !move.MayHitTargetMoreThanOnce && mutableHitTargets.Contains(target.Id)
                    || !CombatGeometry.Contains(move.Shape, state.Origin, state.FacingRadians, target))
                {
                    continue;
                }

                SpatialVector2D direction = CombatGeometry.Direction(state.Origin, target.Position, state.FacingRadians);
                mutableContacts.Add(new CombatContact(
                    state.ActionId,
                    state.Move,
                    state.Source,
                    target.Id,
                    move.Damage,
                    target.Position,
                    direction * move.Knockback));
                mutableHitTargets.Add(target.Id);
            }
            contacts = mutableContacts;
            hitTargets = mutableHitTargets.OrderBy(id => id).ToArray();
        }

        (CombatActionState advanced, string? transitionRuleId) = AdvancePhase(move, state with
        {
            HitTargets = hitTargets,
            ContactTriggered = state.ContactTriggered || contacts.Count > 0
        });
        return new CombatStepResult(
            advanced,
            contacts,
            new CombatPresentationEvent(
                state.ActionId,
                state.Move,
                state.Phase,
                state.PhaseTick,
                advanced.ContactTriggered),
            transitionRuleId);
    }

    private static (CombatActionState State, string? TransitionRuleId) AdvancePhase(
        CombatMoveDefinition move,
        CombatActionState state)
    {
        if (state.Phase == CombatPhase.Complete)
        {
            return (state, null);
        }

        int nextTick = state.PhaseTick + 1;
        int duration = state.Phase switch
        {
            CombatPhase.Startup => move.StartupTicks,
            CombatPhase.Active => move.ActiveTicks,
            CombatPhase.Recovery => move.RecoveryTicks,
            _ => 1
        };
        if (nextTick < duration)
        {
            return (state with { PhaseTick = nextTick }, null);
        }

        TransitionDispatchResult<PhaseState, PhaseEvent, PhaseEffect> transition = PhaseTransitions.Dispatch(
            ToPhaseState(state.Phase),
            new PhaseElapsed(),
            new PhaseContext());
        return (
            state with { Phase = transition.NextState.Phase, PhaseTick = 0 },
            transition.Inspection.SelectedRuleId);
    }

    private static TransitionDefinition<PhaseState, PhaseEvent, PhaseContext, PhaseEffect> DefinePhaseTransitions()
    {
        TransitionScope<PhaseState, PhaseEvent, PhaseContext, PhaseEffect> flow
            = Transition.For<PhaseState, PhaseEvent, PhaseContext, PhaseEffect>();
        return flow.Define(
        [
            flow.On<StartupState, PhaseElapsed>(
                "combat.startup-complete",
                static (_, _, _) => new TransitionOutput<PhaseState, PhaseEffect>(new ActiveState())),
            flow.On<ActiveState, PhaseElapsed>(
                "combat.active-complete",
                static (_, _, _) => new TransitionOutput<PhaseState, PhaseEffect>(new RecoveryState())),
            flow.On<RecoveryState, PhaseElapsed>(
                "combat.recovery-complete",
                static (_, _, _) => new TransitionOutput<PhaseState, PhaseEffect>(new CompleteState()))
        ],
        unmatched: UnmatchedEventBehavior.Reject);
    }

    private static PhaseState ToPhaseState(CombatPhase phase)
    {
        return phase switch
        {
            CombatPhase.Startup => new StartupState(),
            CombatPhase.Active => new ActiveState(),
            CombatPhase.Recovery => new RecoveryState(),
            CombatPhase.Complete => new CompleteState(),
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }

    private abstract record PhaseState(CombatPhase Phase);
    private sealed record StartupState() : PhaseState(CombatPhase.Startup);
    private sealed record ActiveState() : PhaseState(CombatPhase.Active);
    private sealed record RecoveryState() : PhaseState(CombatPhase.Recovery);
    private sealed record CompleteState() : PhaseState(CombatPhase.Complete);
    private abstract record PhaseEvent;
    private sealed record PhaseElapsed : PhaseEvent;
    private sealed record PhaseContext;
    private sealed record PhaseEffect;
}

internal static class CombatGeometry
{
    private const double Epsilon = 1e-9;

    public static void Validate(AttackShape shape)
    {
        bool valid = shape switch
        {
            PointAttackShape point => double.IsFinite(point.Radius) && point.Radius >= 0,
            LineAttackShape line => double.IsFinite(line.Length) && line.Length >= 0
                && double.IsFinite(line.Width) && line.Width >= 0,
            ArcAttackShape arc => double.IsFinite(arc.Radius) && arc.Radius >= 0
                && double.IsFinite(arc.ArcRadians) && arc.ArcRadians > 0 && arc.ArcRadians <= Math.PI * 2,
            RingAttackShape ring => double.IsFinite(ring.Radius) && ring.Radius >= 0
                && double.IsFinite(ring.Width) && ring.Width >= 0,
            _ => false
        };
        if (!valid)
        {
            throw new ArgumentOutOfRangeException(nameof(shape), "Attack shape values must be finite and bounded.");
        }
    }

    public static bool Contains(
        AttackShape shape,
        SpatialPoint2D origin,
        double facing,
        CombatTarget target)
    {
        double deltaX = target.Position.X - origin.X;
        double deltaY = target.Position.Y - origin.Y;
        double distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        return shape switch
        {
            PointAttackShape point => distance <= point.Radius + target.Radius + Epsilon,
            RingAttackShape ring => Math.Abs(distance - ring.Radius) <= (ring.Width / 2) + target.Radius + Epsilon,
            ArcAttackShape arc => distance <= arc.Radius + target.Radius + Epsilon
                && AngleDifference(Math.Atan2(deltaY, deltaX), facing) <= (arc.ArcRadians / 2) + Epsilon,
            LineAttackShape line => InLine(deltaX, deltaY, facing, line, target.Radius),
            _ => false
        };
    }

    public static SpatialVector2D Direction(SpatialPoint2D origin, SpatialPoint2D target, double facing)
    {
        SpatialVector2D delta = target - origin;
        double length = Math.Sqrt(delta.LengthSquared);
        if (length <= Epsilon)
        {
            return new SpatialVector2D(Math.Cos(facing), Math.Sin(facing));
        }
        return delta * (1 / length);
    }

    private static bool InLine(double deltaX, double deltaY, double facing, LineAttackShape line, double targetRadius)
    {
        double forward = (deltaX * Math.Cos(facing)) + (deltaY * Math.Sin(facing));
        double lateral = Math.Abs((-deltaX * Math.Sin(facing)) + (deltaY * Math.Cos(facing)));
        return forward >= -targetRadius - Epsilon
            && forward <= line.Length + targetRadius + Epsilon
            && lateral <= (line.Width / 2) + targetRadius + Epsilon;
    }

    private static double AngleDifference(double left, double right)
    {
        double difference = left - right;
        while (difference > Math.PI)
        {
            difference -= Math.PI * 2;
        }
        while (difference < -Math.PI)
        {
            difference += Math.PI * 2;
        }
        return Math.Abs(difference);
    }
}
