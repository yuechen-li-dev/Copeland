using Aurelian.Strategy;
using Aurelian.StrategyDemo;
using Xunit;

namespace Aurelian.Strategy.Tests;

public sealed class StrategyTests
{
    [Fact]
    public void Gather_construction_production_and_replay_use_real_resolver()
    {
        StrategySession first = StrategyProof.Scenario([TimeSpan.FromMilliseconds(100)]);
        StrategySession partitioned = StrategyProof.Scenario([TimeSpan.FromMilliseconds(17), TimeSpan.FromMilliseconds(83)]);
        Assert.True(first.ObjectiveComplete);
        Assert.Equal(5, first.Units.Count);
        Assert.Equal(first.Hash(), partitioned.Hash());
        var replay = new StrategySession();
        foreach (RecordedIntent item in first.Tape)
        {
            replay.Dispatch(item.Intent);
        }
        Assert.Equal(first.Hash(), replay.Hash());
    }

    [Fact]
    public void Fresh_ranged_and_resource_extensions_share_behavior_and_replay()
    {
        Assert.True(FreshSystemProof.Run().Passed);
    }

    [Fact]
    public void Invalid_visibility_update_is_atomic_and_restore_is_detached()
    {
        var grid = new VisibilityGrid(10, 10);
        grid.Recompute([new(0, 0, 3)]);
        int count = grid.ExploredCount;
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Recompute([new(5, 5, 2), new(-1, 0, 2)]));
        Assert.Equal(count, grid.ExploredCount);
        Assert.Equal(CellVisibility.Visible, grid[0, 0]);
        bool[] saved = grid.CaptureExploration();
        grid.RestoreExploration(saved);
        saved[0] = false;
        Assert.Equal(CellVisibility.Explored, grid[0, 0]);
        grid.Recompute([]);
        Assert.Equal(CellVisibility.Explored, grid[0, 0]);
    }

    [Fact]
    public void Generic_selection_works_for_non_game_identifiers_and_prunes_groups()
    {
        var selection = new EntitySelection<string>();
        selection.Apply(["bed-c", "bed-a", "bed-a"], SelectionChange.Replace, _ => true);
        selection.StoreGroup(2);
        selection.Apply(["bed-a"], SelectionChange.Subtract, _ => true);
        Assert.Equal(["bed-c"], selection.Snapshot());
        selection.Prune(id => id != "bed-a");
        selection.RecallGroup(2, SelectionChange.Replace, _ => true);
        Assert.Equal(["bed-c"], selection.Snapshot());
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.StoreGroup(0));
    }

    [Fact]
    public void Insufficient_cost_and_occupied_placement_do_not_mutate_state()
    {
        var session = new StrategySession();
        string before = session.Hash();
        Assert.False(session.Dispatch(new BuildIntent(StrategySession.Home)).Accepted);
        Assert.Equal(before, session.Hash());
        for (int index = 0; index < 4; index++)
        {
            Assert.True(session.Dispatch(new ProduceIntent(UnitKind.Worker)).Accepted);
        }
        before = session.Hash();
        Assert.False(session.Dispatch(new ProduceIntent(UnitKind.Worker)).Accepted);
        Assert.Equal(before, session.Hash());
    }

    [Fact]
    public void Placement_requires_exploration_of_the_entire_footprint()
    {
        var session = new StrategySession();
        Assert.NotEqual(CellVisibility.Unknown, session.Fog[16, 15]);
        string before = session.Hash();
        Assert.False(session.Dispatch(new BuildIntent(new(16, 15))).Accepted);
        Assert.Equal(before, session.Hash());
    }

    [Fact]
    public void Replay_tape_is_detached_from_callers_and_readers()
    {
        var session = new StrategySession();
        int[] ids = [3];
        session.Dispatch(new OrderIntent(ids, new GatherOrder(100)));
        ids[0] = 999;
        ((OrderIntent)session.Tape[0].Intent).Ids[0] = 999;
        var replay = new StrategySession();
        foreach (RecordedIntent entry in session.Tape)
        {
            replay.Dispatch(entry.Intent);
        }
        Assert.Equal(session.Hash(), replay.Hash());
    }

    [Fact]
    public void Farming_style_preserves_semantic_layout()
    {
        Assert.True(FreshPresentationProof.Run().IdenticalNodeIdsAndRectangles);
    }
}
