using Aurelian.Combat;

namespace TinyFarm.Core;

public static class TinyFarmCombatMoves
{
    private const double FullCircle = Math.PI * 2;

    public static CombatMoveDefinition SwordSwing { get; } = new(
        new CombatMoveId("tinyfarm.sword.swing"),
        Damage: 1,
        Shape: new ArcAttackShape(TinyFarmSpatialQueries.InteractionRangeUnits, 2.72),
        StartupTicks: 3,
        ActiveTicks: 5,
        RecoveryTicks: 10,
        Knockback: 1,
        Lunge: 76);

    public static CombatMoveDefinition SpearThrust { get; } = new(
        new CombatMoveId("tinyfarm.spear.thrust"),
        Damage: 1,
        Shape: new LineAttackShape(2.5 * ScenePosition.UnitsPerTile, ScenePosition.UnitsPerTile / 2d),
        StartupTicks: 5,
        ActiveTicks: 4,
        RecoveryTicks: 13,
        Knockback: 0.78,
        Lunge: 88);

    public static CombatMoveDefinition HammerSmash { get; } = new(
        new CombatMoveId("tinyfarm.hammer.smash"),
        Damage: 2,
        Shape: new RingAttackShape(ScenePosition.UnitsPerTile, ScenePosition.UnitsPerTile),
        StartupTicks: 8,
        ActiveTicks: 4,
        RecoveryTicks: 23,
        Knockback: 1.72,
        Lunge: 58);

    public static CombatMoveDefinition TwinStrike { get; } = new(
        new CombatMoveId("tinyfarm.pruning-shears.twin-strike"),
        Damage: 0.84,
        Shape: new ArcAttackShape(ScenePosition.UnitsPerTile, 2.34),
        StartupTicks: 2,
        ActiveTicks: 5,
        RecoveryTicks: 9,
        Knockback: 0.68,
        Lunge: 91);

    public static CombatMoveDefinition SweepingHoe { get; } = new(
        new CombatMoveId("tinyfarm.hoe.sweep"),
        Damage: 0.75,
        Shape: new ArcAttackShape(1.5 * ScenePosition.UnitsPerTile, FullCircle * 0.42),
        StartupTicks: 4,
        ActiveTicks: 4,
        RecoveryTicks: 12,
        Knockback: 0.9);
}
