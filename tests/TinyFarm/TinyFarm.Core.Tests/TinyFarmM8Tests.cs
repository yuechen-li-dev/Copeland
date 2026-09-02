using System.Diagnostics;
using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM8Tests
{
    private static readonly ActorId[] Npcs =
    [
        TinyFarmIds.Elias,
        TinyFarmIds.Mara,
        TinyFarmIds.Sela
    ];

    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void ScheduleTable_CapturesTheCompleteLegacyLaw()
    {
        Assert.Collection(
            definitions.Schedules.Windows,
            window => AssertWindow(window, TinyFarmIds.Elias, null, 0, 720, TinyFarmAnchorIds.FarmWorkArea, 0),
            window => AssertWindow(window, TinyFarmIds.Elias, null, 720, 1080, TinyFarmAnchorIds.RiversideMeetingPoint, 0),
            window => AssertWindow(window, TinyFarmIds.Elias, null, 1080, 1440, TinyFarmAnchorIds.FarmWorkArea, 0),
            window => AssertWindow(window, TinyFarmIds.Mara, null, 0, 720, TinyFarmAnchorIds.TownSquare, 0),
            window => AssertWindow(window, TinyFarmIds.Mara, null, 720, 1020, TinyFarmAnchorIds.RiversideMeetingPoint, 0),
            window => AssertWindow(window, TinyFarmIds.Mara, null, 1020, 1440, TinyFarmAnchorIds.FarmHome, 0),
            window => AssertWindow(window, TinyFarmIds.Mara, 6, 540, 1020, TinyFarmAnchorIds.StoreCounter, 1),
            window => AssertWindow(window, TinyFarmIds.Mara, 7, 600, 1020, TinyFarmAnchorIds.RiversideMeetingPoint, 1),
            window => AssertWindow(window, TinyFarmIds.Sela, null, 0, 480, TinyFarmAnchorIds.FarmHome, 0),
            window => AssertWindow(window, TinyFarmIds.Sela, null, 480, 1080, TinyFarmAnchorIds.StoreCounter, 0),
            window => AssertWindow(window, TinyFarmIds.Sela, null, 1080, 1440, TinyFarmAnchorIds.FarmHome, 0));
    }

    [Fact]
    public void DominatusSchedule_MatchesLegacyForEveryNpcMinuteAcrossSevenDays()
    {
        foreach (ActorId actor in Npcs)
        {
            for (int minute = 0; minute < 7 * 1440; minute++)
            {
                Assert.Equal(LegacyScheduledAnchor(actor, minute), Decide(actor, minute));
            }
        }
    }

    [Fact]
    public void EveryTransition_PreservesMinuteBeforeAtAndAfterWithStableTieBreaks()
    {
        foreach (ActorId actor in Npcs)
        {
            for (int minute = 1; minute < 7 * 1440 - 1; minute++)
            {
                if (LegacyScheduledAnchor(actor, minute - 1) == LegacyScheduledAnchor(actor, minute))
                {
                    continue;
                }

                for (int offset = -1; offset <= 1; offset++)
                {
                    int observedMinute = minute + offset;
                    SceneAnchorId expected = LegacyScheduledAnchor(actor, observedMinute);
                    Assert.Equal(expected, Decide(actor, observedMinute));
                    Assert.Equal(expected, Decide(actor, observedMinute));
                }
            }
        }
    }

    [Fact]
    public void ScheduleDecision_IsSemanticInspectableAndRejectsUnknownActors()
    {
        TinyFarmScheduleDecision decision = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            5 * 1440 + 540);

        Assert.Equal(TinyFarmNpcSchedule.ScheduleDecisionSlot, decision.DecisionSlot);
        Assert.Equal(TinyFarmAnchorIds.StoreCounter, decision.SelectedAnchor);
        Assert.Equal("day-6-store", decision.Reason);
        Assert.Equal(540, decision.WindowStart);
        Assert.Equal(1020, decision.WindowEnd);
        Assert.Equal(1, decision.Priority);
        Assert.Throws<KeyNotFoundException>(() => TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            new ActorId("unknown"),
            600));
    }

    [Fact]
    public void ActiveNpc_ReplansWhenScheduleBoundaryChangesGoal()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 719;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Farm, At(16, 6));
        SetPlacement(state, TinyFarmIds.Elias, TinyFarmSceneIds.Farm, At(9, 7));
        var session = new TinyFarmSession(state, definitions);

        session.Step(new LookIntent());
        int plansBeforeBoundary = session.NavigationPlanCount;
        session.Step(new WaitIntent(1));

        Assert.Equal(TinyFarmAnchorIds.RiversideMeetingPoint, Decide(TinyFarmIds.Elias, session.State.Minute));
        Assert.True(session.NavigationPlanCount > plansBeforeBoundary);
    }

    [Fact]
    public void InactiveNpc_UsesSameBoundaryDecisionWithoutPathfinding()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 719;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Overworld, At(6, 6));
        var planner = new CountingPlanner();
        var session = new TinyFarmSession(state, definitions, planner);

        session.Step(new WaitIntent(1));

        Assert.Equal(TinyFarmAnchorIds.RiversideMeetingPoint, Decide(TinyFarmIds.Elias, session.State.Minute));
        Assert.Equal(TinyFarmIds.TownSquare, session.State.Actor(TinyFarmIds.Elias).Location);
        Assert.Equal(0, planner.QueryCount);
        Assert.Equal(0, session.NavigationPlanCount);
    }

    [Fact]
    public void SaveLoadImmediatelyBeforeBoundaryCrossesIdentically()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 719;
        var original = new TinyFarmSession(state, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(original.CaptureWeekSave(), definitions);

        original.Step(new WaitIntent(1));
        loaded.Step(new WaitIntent(1));

        Assert.Equal(TinyFarmSemanticHash.Compute(original.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal(Decide(TinyFarmIds.Elias, original.State.Minute), Decide(TinyFarmIds.Elias, loaded.State.Minute));
    }

    [Fact]
    public void SaveLoadImmediatelyAfterBoundaryRetainsSameRecomputedGoal()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 720;
        var original = new TinyFarmSession(state, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(original.CaptureWeekSave(), definitions);

        original.Step(new LookIntent());
        loaded.Step(new LookIntent());

        Assert.Equal(TinyFarmSemanticHash.Compute(original.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal(TinyFarmAnchorIds.RiversideMeetingPoint, Decide(TinyFarmIds.Elias, loaded.State.Minute));
    }

    [Fact]
    public void DominatusDecisionCostIsBoundedAndDefinitionIsReused()
    {
        object definition = TinyFarmNpcSchedule.Definition;
        for (int index = 0; index < 100; index++)
        {
            _ = LegacyScheduledAnchor(Npcs[index % Npcs.Length], index % 1440);
            _ = Decide(Npcs[index % Npcs.Length], index % 1440);
        }

        var legacyWatch = Stopwatch.StartNew();
        for (int index = 0; index < 1000; index++)
        {
            _ = LegacyScheduledAnchor(Npcs[index % Npcs.Length], index % 1440);
        }
        legacyWatch.Stop();

        var dominatusWatch = Stopwatch.StartNew();
        for (int index = 0; index < 1000; index++)
        {
            _ = Decide(Npcs[index % Npcs.Length], index % 1440);
        }
        dominatusWatch.Stop();

        Assert.Same(definition, TinyFarmNpcSchedule.Definition);
        Assert.True(dominatusWatch.Elapsed < TimeSpan.FromSeconds(5));
        Console.WriteLine(
            $"legacy-1000-ms={legacyWatch.Elapsed.TotalMilliseconds:F4}; dominatus-1000-ms={dominatusWatch.Elapsed.TotalMilliseconds:F4}");
    }

    [Fact]
    public void CanonicalM8Scenario_ProvesOutcomeAAndExactHistoricalHashes()
    {
        TinyFarmM8Proof proof = TinyFarmScheduleScenario.Prove().Proof;

        Assert.Equal("A", proof.Outcome);
        Assert.Equal("dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333", proof.M1Hash);
        Assert.Equal("4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3", proof.M2Hash);
        Assert.Equal("fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa", proof.SceneContentHash);
        Assert.True(proof.ExhaustiveMinuteParity);
        Assert.True(proof.SevenDayParity);
        Assert.True(proof.TransitionBoundaryParity);
        Assert.True(proof.ActiveInactiveParity);
        Assert.True(proof.SaveLoadBeforeTransition);
        Assert.True(proof.SaveLoadAfterTransition);
        Assert.True(proof.SaveLoadWhileMoving);
        Assert.True(proof.StaticDefinitionReused);
    }

    private SceneAnchorId Decide(ActorId actor, int minute)
    {
        return TinyFarmNpcSchedule.Decide(definitions.Schedules, actor, minute).SelectedAnchor;
    }

    private static SceneAnchorId LegacyScheduledAnchor(ActorId actor, int minute)
    {
        int minuteOfDay = minute % 1440;
        int day = minute / 1440 + 1;

        if (actor == TinyFarmIds.Mara)
        {
            if (day == 6 && minuteOfDay >= 540 && minuteOfDay < 1020)
            {
                return TinyFarmAnchorIds.StoreCounter;
            }

            if (day == 7 && minuteOfDay >= 600 && minuteOfDay < 1020)
            {
                return TinyFarmAnchorIds.RiversideMeetingPoint;
            }

            if (minuteOfDay < 720)
            {
                return TinyFarmAnchorIds.TownSquare;
            }

            return minuteOfDay < 1020
                ? TinyFarmAnchorIds.RiversideMeetingPoint
                : TinyFarmAnchorIds.FarmHome;
        }

        if (actor == TinyFarmIds.Elias)
        {
            return minuteOfDay >= 720 && minuteOfDay < 1080
                ? TinyFarmAnchorIds.RiversideMeetingPoint
                : TinyFarmAnchorIds.FarmWorkArea;
        }

        return minuteOfDay >= 480 && minuteOfDay < 1080
            ? TinyFarmAnchorIds.StoreCounter
            : TinyFarmAnchorIds.FarmHome;
    }

    private static void AssertWindow(
        TinyFarmScheduleWindow actual,
        ActorId actor,
        int? day,
        int fromMinute,
        int toMinute,
        SceneAnchorId anchor,
        int priority)
    {
        Assert.Equal(actor, actual.Actor);
        Assert.Equal(day, actual.Day.SpecificDay);
        Assert.Equal(fromMinute, actual.StartMinute);
        Assert.Equal(toMinute, actual.EndMinuteExclusive);
        Assert.Equal(anchor, actual.Anchor);
        Assert.Equal(priority, actual.Priority);
    }

    private static ScenePosition At(int x, int y)
    {
        return ScenePosition.FromGrid(new GridPosition(x, y));
    }

    private static void SetPlacement(
        TinyFarmState state,
        ActorId actor,
        SceneId scene,
        ScenePosition position)
    {
        int placementIndex = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[placementIndex] = new ActorSceneState(actor, scene, position, ActorFacing.Down);
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with
        {
            Location = TinyFarmScenes.LocationForScene(scene)
        };
    }

    private sealed class CountingPlanner : INavigationPlanner
    {
        private readonly DotRecastNavigationPlanner inner = new();

        public int QueryCount { get; private set; }

        public NavigationPath FindPath(SceneDefinition scene, ScenePosition start, ScenePosition goal)
        {
            QueryCount++;
            return inner.FindPath(scene, start, goal);
        }
    }
}
