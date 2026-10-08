using System.Security.Cryptography;
using System.Text;
using Aurelian.Combat;
using Aurelian.Field2D;
using Aurelian.Spatial2D;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class HollowfluxPearlM20Tests
{
    [Fact]
    public void PresentationDescribesThePhaseThatProducedContacts()
    {
        CombatMoveDefinition move = TinyFarmCombatMoves.SwordSwing;
        CombatActionState active = CombatActionState.Start(
            1,
            move,
            new CombatActorId("player"),
            new SpatialPoint2D(0, 0),
            0) with
        {
            Phase = CombatPhase.Active,
            PhaseTick = move.ActiveTicks - 1
        };

        CombatStepResult result = CombatResolver.Advance(
            move,
            active,
            [new CombatTarget(new CombatActorId("slime"), new SpatialPoint2D(100, 0))]);

        Assert.Single(result.Contacts);
        Assert.Equal(CombatPhase.Active, result.Presentation.Phase);
        Assert.Equal(move.ActiveTicks - 1, result.Presentation.PhaseTick);
        Assert.Equal(CombatPhase.Recovery, result.State.Phase);
    }

    [Fact]
    public void RecoveredPhaseMachineMatchesHollowfluxBoundedFixture()
    {
        CombatMoveDefinition move = TinyFarmCombatMoves.SwordSwing;
        CombatActionState state = CombatActionState.Start(
            1,
            move,
            new CombatActorId("player"),
            new SpatialPoint2D(0, 0),
            0);
        var transitions = new List<string>();
        var transitionRules = new List<string>();
        CombatPhase previous = state.Phase;

        while (state.Phase != CombatPhase.Complete)
        {
            CombatStepResult step = CombatResolver.Advance(move, state, []);
            state = step.State;
            if (step.PhaseTransitionRuleId is string ruleId)
            {
                transitionRules.Add(ruleId);
            }
            if (state.Phase != previous)
            {
                transitions.Add($"{previous}->{state.Phase}");
                previous = state.Phase;
            }
        }

        Assert.Equal(
            ["Startup->Active", "Active->Recovery", "Recovery->Complete"],
            transitions);
        Assert.Equal(18, move.StartupTicks + move.ActiveTicks + move.RecoveryTicks);
        Assert.Equal(
            ["combat.startup-complete", "combat.active-complete", "combat.recovery-complete"],
            transitionRules);
    }

    [Fact]
    public void OneSwingHitsEachDistinctTargetOnce()
    {
        CombatMoveDefinition move = TinyFarmCombatMoves.SwordSwing with
        {
            StartupTicks = 1,
            ActiveTicks = 3,
            RecoveryTicks = 1
        };
        CombatActionState state = CombatActionState.Start(
            4,
            move,
            new CombatActorId("player"),
            new SpatialPoint2D(0, 0),
            0);
        CombatTarget[] targets =
        [
            new(new CombatActorId("a"), new SpatialPoint2D(700, -100)),
            new(new CombatActorId("b"), new SpatialPoint2D(700, 100))
        ];
        var contacts = new List<CombatContact>();

        while (state.Phase != CombatPhase.Complete)
        {
            CombatStepResult step = CombatResolver.Advance(move, state, targets);
            contacts.AddRange(step.Contacts);
            state = step.State;
        }

        Assert.Equal(["a", "b"], contacts.Select(contact => contact.Target.Value));
    }

    [Fact]
    public void FreshCombatExtensionsAreDataOnlyAndUseExistingShapes()
    {
        Assert.IsType<LineAttackShape>(TinyFarmCombatMoves.SpearThrust.Shape);
        Assert.IsType<RingAttackShape>(TinyFarmCombatMoves.HammerSmash.Shape);
        Assert.IsType<ArcAttackShape>(TinyFarmCombatMoves.TwinStrike.Shape);
        Assert.IsType<ArcAttackShape>(TinyFarmCombatMoves.SweepingHoe.Shape);
    }

    [Fact]
    public void DisturbanceCannotCrossDisconnectedLiquid()
    {
        ReactiveFluid2D field = ConnectedPond();
        int affected = field.ApplyDisturbance(new FieldDisturbance(
            FieldDisturbanceShape.Point,
            X: 1.5,
            Y: 2.5,
            Strength: 1,
            Radius: 8));

        Assert.True(affected > 0);
        Assert.True(field.HeightField[1, 2] > 0);
        Assert.Equal(0, field.HeightField[5, 2]);
    }

    [Fact]
    public void ChargeUsesConductiveLiquidAndNeverLeaksAcrossDryBarrier()
    {
        ReactiveFluid2D field = ConnectedPond();
        int energized = field.Energize(1.5, 2.5, 1, 8);
        field.AdvanceFrame(1.0 / 60.0);

        Assert.True(energized > 0);
        Assert.True(field.Charge[1, 2] > 0);
        Assert.Equal(0, field.Charge[3, 2]);
        Assert.Equal(0, field.Charge[5, 2]);
    }

    [Fact]
    public void FixedStepIsDeterministicAcrossFramePartitions()
    {
        ReactiveFluid2D first = ConnectedPond();
        ReactiveFluid2D second = ConnectedPond();
        FieldDisturbance disturbance = new(
            FieldDisturbanceShape.Ring,
            X: 1.5,
            Y: 2.5,
            Strength: 0.8,
            Radius: 1.5,
            Width: 1);
        first.ApplyDisturbance(disturbance);
        second.ApplyDisturbance(disturbance);

        for (int index = 0; index < 60; index++)
        {
            first.AdvanceFrame(1.0 / 60.0);
        }
        for (int index = 0; index < 20; index++)
        {
            second.AdvanceFrame(1.0 / 20.0);
        }

        Assert.Equal(Hash(first.Snapshot()), Hash(second.Snapshot()));
    }

    [Fact]
    public void TinyFarmHammerContactEmitsFieldDisturbanceThenFieldAffectsGameplayQuery()
    {
        ReactiveFluid2D pond = ConnectedPond();
        CombatMoveDefinition hammer = TinyFarmCombatMoves.HammerSmash with
        {
            StartupTicks = 1,
            ActiveTicks = 1,
            RecoveryTicks = 1,
            Shape = new RingAttackShape(1, 2)
        };
        CombatActionState action = CombatActionState.Start(
            7,
            hammer,
            new CombatActorId("player"),
            new SpatialPoint2D(1.5, 2.5),
            0);
        CombatTarget target = new(new CombatActorId("slime"), new SpatialPoint2D(2.5, 2.5));
        action = CombatResolver.Advance(hammer, action, [target]).State;
        CombatStepResult active = CombatResolver.Advance(hammer, action, [target]);
        CombatContact contact = Assert.Single(active.Contacts);

        int rippled = pond.ApplyDisturbance(new FieldDisturbance(
            FieldDisturbanceShape.Ring,
            contact.Point.X,
            contact.Point.Y,
            contact.Damage,
            Radius: 1,
            Width: 2,
            SemanticPayload: contact.Move.Value));
        pond.Energize(contact.Point.X, contact.Point.Y, 1, 2);
        float chargedWaterDamage = pond.Sample(contact.Point.X, contact.Point.Y).Charge >= 0.25f ? 1 : 0;

        Assert.True(rippled > 0);
        Assert.Equal(1, chargedWaterDamage);
    }

    private static ReactiveFluid2D ConnectedPond()
    {
        var field = new ReactiveFluid2D(7, 5, 1);
        for (int y = 0; y < field.LiquidMask.Height; y++)
        {
            for (int x = 0; x < field.LiquidMask.Width; x++)
            {
                bool liquid = x <= 2 || x >= 4;
                field.LiquidMask[x, y] = liquid ? (byte)1 : (byte)0;
                field.ConductiveMask[x, y] = liquid ? (byte)1 : (byte)0;
            }
        }
        return field;
    }

    private static string Hash(ReactiveFluidSnapshot snapshot)
    {
        var text = new StringBuilder();
        text.Append(snapshot.Tick).Append('|');
        foreach (float value in snapshot.HeightField)
        {
            text.Append(BitConverter.SingleToInt32Bits(value)).Append(',');
        }
        foreach (float value in snapshot.Velocity)
        {
            text.Append(BitConverter.SingleToInt32Bits(value)).Append(',');
        }
        foreach (float value in snapshot.Charge)
        {
            text.Append(BitConverter.SingleToInt32Bits(value)).Append(',');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
