using Aurelian.Spatial2D;

namespace Aurelian.StrategyDemo;

/// <summary>Exercises the extension through the same intents and behavior ticks as the playable sample.</summary>
public static class FreshSystemProof
{
    public static FreshSystemProofResult Run()
    {
        StrategySession ranged = ProveRangedOrders();
        StrategySession wood = ProveWoodGathering();
        StrategySession switched = ProveCargoSwitching();
        ProveReplay(ranged);
        ProveReplay(wood);
        ProveReplay(switched);
        return new(
            true,
            [
                "Ranger recruitment reserves crystal and completes through production ticks.",
                "Ranger selection and control-group recall preserve the live unit ID.",
                "Ranger moves through the ordinary MoveOrder and attacks beyond soldier range.",
                "Invalid target, fog, ownership, position and gather orders reject without semantic mutation.",
                "Worker harvests wood and deposits it into wood stock through the existing HFSM.",
                "Switching resource orders deposits existing cargo without changing its resource kind.",
                "All three accepted/rejected intent tapes replay to identical semantic hashes.",
            ],
            ranged.Hash(),
            wood.Hash(),
            switched.Hash(),
            wood.WoodGathered,
            switched.WoodGathered,
            switched.Gathered);
    }

    private static StrategySession ProveRangedOrders()
    {
        var session = new StrategySession();
        RequireAccepted(session, new ProduceIntent(UnitKind.Ranger));
        Require(session.Stock == 75 && session.Production.Count == 1, "Ranger production must reserve the normal cost.");
        AdvanceUntil(session, () => session.Units.Any(unit => unit.Kind == UnitKind.Ranger), 60, "Ranger production");
        StrategyUnit ranger = session.Units.Single(unit => unit.Kind == UnitKind.Ranger);

        RequireAccepted(session, new SelectIntent([ranger.Id]));
        RequireAccepted(session, new ControlGroupIntent(2, true));
        RequireAccepted(session, new SelectIntent([1]));
        RequireAccepted(session, new ControlGroupIntent(2, false));
        Require(session.Selection.Snapshot().SequenceEqual([ranger.Id]), "Ranger control-group recall must restore its selection.");

        RequireRejected(session, new OrderIntent([ranger.Id], new GatherOrder(102)));
        RequireRejected(session, new OrderIntent([3, ranger.Id], new GatherOrder(102)));
        RequireRejected(session, new OrderIntent([ranger.Id, 999], new MoveOrder(new(15, 11))));
        RequireRejected(session, new OrderIntent([ranger.Id], new MoveOrder(new(-1, 11))));
        RequireRejected(session, new OrderIntent([ranger.Id], new AttackOrder(300)));

        SpatialPoint2D destination = new(15, 11);
        RequireAccepted(session, new OrderIntent([ranger.Id], new MoveOrder(destination)));
        AdvanceUntil(session, () => ranger.Order is IdleOrder, 100, "Ranger movement");
        Require((ranger.Position - destination).LengthSquared < .01, "Ranger must reach its requested destination.");
        RequireRejected(session, new OrderIntent([ranger.Id], new AttackOrder(999)));

        SpatialPoint2D firingPosition = ranger.Position;
        double firingDistanceSquared = (firingPosition - StrategySession.EnemyPosition).LengthSquared;
        Require(firingDistanceSquared > 1.4 * 1.4 && firingDistanceSquared <= 4.2 * 4.2,
            "Ranger proof must fire beyond soldier range and within ranger range.");
        RequireAccepted(session, new OrderIntent([ranger.Id], new AttackOrder(300)));
        AdvanceUntil(session, () => session.EnemyHealth < 40, 30, "First ranged damage");
        Require(ranger.Position == firingPosition, "Ranger should inflict ranged damage without walking into melee range.");
        AdvanceUntil(session, () => session.EnemyHealth == 0, 150, "Ranger combat completion");
        Require(ranger.Order is IdleOrder, "Ranger must return to idle after defeating its target.");
        RequireRejected(session, new OrderIntent([ranger.Id], new AttackOrder(300)));
        return session;
    }

    private static StrategySession ProveWoodGathering()
    {
        var session = new StrategySession();
        ResourceNode node = session.Resources.Single(resource => resource.Kind == ResourceKind.Wood);
        int originalAmount = node.Amount;
        RequireRejected(session, new OrderIntent([3], new GatherOrder(999)));
        RequireAccepted(session, new OrderIntent([3], new GatherOrder(node.Id)));
        AdvanceUntil(session, () => session.WoodGathered >= 10, 300, "Wood harvest and dropoff");
        StrategyUnit worker = session.Units.Single(unit => unit.Id == 3);
        Require(session.WoodStock == 10 && session.WoodGathered == 10, "Wood dropoff must credit the wood counters.");
        Require(session.Stock == 100 && session.Gathered == 0, "Wood must preserve crystal stock and crystal objective progress.");
        Require(node.Amount == originalAmount - 10, "Wood harvested must equal wood deposited.");
        Require(worker.Carry == 0 && worker.CarryKind is null, "Dropoff must clear cargo amount and kind.");
        return session;
    }

    private static StrategySession ProveCargoSwitching()
    {
        var session = new StrategySession();
        StrategyUnit worker = session.Units.Single(unit => unit.Id == 3);
        RequireAccepted(session, new OrderIntent([worker.Id], new GatherOrder(102)));
        AdvanceUntil(session, () => worker.Carry > 0, 100, "Partial wood cargo");
        int woodCargo = worker.Carry;
        Require(worker.CarryKind == ResourceKind.Wood, "Harvested wood must carry its resource identity.");
        RequireAccepted(session, new OrderIntent([worker.Id], new GatherOrder(100)));
        AdvanceUntil(session, () => session.WoodGathered == woodCargo, 200, "Wood dropoff after resource-order switch");
        Require(session.Gathered == 0, "Carried wood must not become crystal after a changed order.");
        AdvanceUntil(session, () => session.Gathered >= 10, 300, "Crystal harvest after wood dropoff");
        Require(session.Stock == 110 && session.WoodStock == woodCargo, "Separate resource balances must survive order switching.");
        Require(session.Resources.Single(node => node.Id == 102).Amount == 300 - woodCargo,
            "Switching orders must not continue harvesting the old node.");
        return session;
    }

    private static void ProveReplay(StrategySession original)
    {
        var replay = new StrategySession();
        foreach (RecordedIntent recorded in original.Tape)
        {
            replay.Dispatch(recorded.Intent);
        }
        Require(original.Hash() == replay.Hash(), "Extension tape must replay deterministically.");
        Require(original.Selection.Snapshot().SequenceEqual(replay.Selection.Snapshot()), "Replay must preserve selection.");
    }

    private static void RequireAccepted(StrategySession session, StrategyIntent intent)
    {
        IntentResult result = session.Dispatch(intent);
        Require(result.Accepted, $"Expected accepted {intent}: {result.Message}");
    }

    private static void RequireRejected(StrategySession session, StrategyIntent intent)
    {
        string before = session.Hash();
        IntentResult result = session.Dispatch(intent);
        Require(!result.Accepted, $"Expected rejected {intent}.");
        Require(session.Hash() == before, $"Rejected {intent} must preserve semantic state.");
    }

    private static void AdvanceUntil(StrategySession session, Func<bool> condition, int limit, string scenario)
    {
        for (int step = 0; step < limit && !condition(); step++)
        {
            session.Dispatch(new AdvanceIntent());
        }
        Require(condition(), $"{scenario} did not complete within {limit} semantic ticks.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed record FreshSystemProofResult(
    bool Passed,
    string[] Checks,
    string RangedHash,
    string WoodHash,
    string SwitchedCargoHash,
    int WoodDeposited,
    int SwitchedWoodDeposited,
    int CrystalDepositedAfterSwitch);
