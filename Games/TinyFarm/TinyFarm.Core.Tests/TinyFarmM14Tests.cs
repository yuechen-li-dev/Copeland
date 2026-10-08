using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM14Tests
{
    [Fact]
    public void ActiveNpcMovesBeforeWorldMinuteWithoutPolicyOrPathChurn()
    {
        TinyFarmSimulationHost host = CreateWalkingHost();
        ScenePosition before = EliasPosition(host);
        int energy = host.Session.State.EnergyFor(TinyFarmIds.Elias).Energy;
        long decisions = host.Session.DecisionEvaluationCount;
        int queries = host.Session.NavigationPlanCount;

        host.AdvanceHostTime(TimeSpan.FromSeconds(1));

        Assert.NotEqual(before, EliasPosition(host));
        Assert.Equal(0, host.WorldMinutesAdvanced);
        Assert.Equal(energy, host.Session.State.EnergyFor(TinyFarmIds.Elias).Energy);
        Assert.Equal(decisions, host.Session.DecisionEvaluationCount);
        Assert.Equal(queries, host.Session.NavigationPlanCount);
        Assert.True(host.NpcLocomotionReductions > 0);
    }

    [Fact]
    public void MovingNpcIsEquivalentAtSixtyAndOneHundredFortyFourRenderHertz()
    {
        TinyFarmSimulationHost sixty = CreateWalkingHost();
        TinyFarmSimulationHost oneFortyFour = CreateWalkingHost();
        Advance(sixty, TimeSpan.FromSeconds(10), 600);
        Advance(oneFortyFour, TimeSpan.FromSeconds(10), 1_440);
        AssertEquivalent(sixty, oneFortyFour);
    }

    [Fact]
    public void MovingNpcIsEquivalentUnderIrregularPartition()
    {
        TinyFarmSimulationHost irregular = CreateWalkingHost();
        TinyFarmSimulationHost even = CreateWalkingHost();
        int[] milliseconds = [16, 16, 50, 3, 91, 7, 33, 84];
        long ticks = 0;
        for (int repeat = 0; repeat < 25; repeat++)
        {
            foreach (int value in milliseconds)
            {
                TimeSpan delta = TimeSpan.FromMilliseconds(value);
                irregular.AdvanceHostTime(delta);
                ticks += delta.Ticks;
            }
        }
        Advance(even, TimeSpan.FromTicks(ticks), 317);
        AssertEquivalent(irregular, even);
    }

    [Fact]
    public void PauseFreezesAndResumeContinuesNpcLocomotion()
    {
        TinyFarmSimulationHost host = CreateWalkingHost();
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        ScenePosition beforePause = EliasPosition(host);
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Paused));
        host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        Assert.Equal(beforePause, EliasPosition(host));
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        Assert.NotEqual(beforePause, EliasPosition(host));
    }

    [Fact]
    public void FastForwardScalesTheSharedLocomotionDomainExactly()
    {
        TinyFarmSimulationHost play = CreateWalkingHost();
        TinyFarmSimulationHost fast = CreateWalkingHost();
        fast.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.FastForward));
        play.AdvanceHostTime(TimeSpan.FromSeconds(1));
        fast.AdvanceHostTime(TimeSpan.FromSeconds(1));
        Assert.Equal(60, play.LocomotionStepsAdvanced);
        Assert.Equal(600, fast.LocomotionStepsAdvanced);
        Assert.True(fast.NpcLocomotionReductions > play.NpcLocomotionReductions);
    }

    [Fact]
    public void WanderSelectionIsLocalDeterministicAndEnergyBounded()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmScheduleDecision first = Decide(definitions, TinyFarmAnchorIds.FarmHome, 9_000, 480);
        TinyFarmScheduleDecision repeat = Decide(definitions, TinyFarmAnchorIds.FarmHome, 9_000, 480);
        TinyFarmScheduleDecision rotate = Decide(definitions, TinyFarmAnchorIds.FarmWanderA, 9_000, 480);
        TinyFarmScheduleDecision tired = Decide(definitions, TinyFarmAnchorIds.FarmWanderA, 1_000, 480);
        TinyFarmScheduleDecision required = Decide(definitions, TinyFarmAnchorIds.FarmWanderA, 10_000, 1320);

        Assert.Equal(TinyFarmAnchorIds.FarmWanderA, first.SelectedAnchor);
        Assert.Equal(first.SelectedAnchor, repeat.SelectedAnchor);
        Assert.Equal(
            first.UtilityScores.Select(score => score.Score),
            repeat.UtilityScores.Select(score => score.Score));
        Assert.Equal(TinyFarmAnchorIds.FarmWanderB, rotate.SelectedAnchor);
        Assert.Equal(TinyFarmAnchorIds.EliasHomeBed, tired.SelectedAnchor);
        Assert.Equal(TinyFarmAnchorIds.EliasHomeBed, required.SelectedAnchor);
        Assert.Equal(TinyFarmSceneIds.Farm, definitions.Scenes.GetAnchor(first.SelectedAnchor).Scene);
        Assert.Equal(SceneAnchorKind.Wander, definitions.Scenes.GetAnchor(first.SelectedAnchor).Kind);
    }

    [Fact]
    public void WanderGoalRemainsCommittedBetweenMinuteEvaluationsUntilArrival()
    {
        TinyFarmSimulationHost host = CreateWalkingHost();
        SceneAnchorId? committed = host.Session.NavigationTargetFor(TinyFarmIds.Elias);
        host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        Assert.Equal(committed, host.Session.NavigationTargetFor(TinyFarmIds.Elias));
    }

    [Fact]
    public void InactiveNpcConsumesNoFixedStepLocomotionOrPathQueries()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM14ControlStates.Create(definitions, "wander");
        int player = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        SceneAnchorDefinition residence = definitions.Scenes.GetAnchor(new SceneAnchorId("residence.from-farm"));
        state.MutableActorScenes[player] = new ActorSceneState(TinyFarmIds.Player, residence.Scene, residence.Position);
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(state, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        host.Session.Step(new LookIntent());
        ScenePosition afterCoarseDecision = EliasPosition(host);
        host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        Assert.Equal(afterCoarseDecision, EliasPosition(host));
        Assert.Equal(0, host.NpcLocomotionReductions);
        Assert.Equal(0, host.Session.NavigationPlanCount);
    }

    [Fact]
    public void SaveLoadMidWanderReplansDerivedPathAndContinues()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateWalkingHost(definitions);
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        byte[] save = host.Session.CaptureWeekSave();
        ScenePosition saved = EliasPosition(host);
        var restored = new TinyFarmSimulationHost(
            TinyFarmChunkedSaveCodec.Read(save, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        restored.Session.Step(new LookIntent());
        Assert.Equal(1, restored.Session.NavigationPlanCount);
        restored.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        Assert.NotEqual(saved, EliasPosition(restored));
    }

    [Fact]
    public void AnchorArrivalIsRecognizedInsideTheLocomotionDomain()
    {
        TinyFarmSimulationHost host = CreateWalkingHost();
        host.AdvanceHostTime(TimeSpan.FromSeconds(3));
        Assert.Equal(0, host.WorldMinutesAdvanced);
        Assert.True(host.AnchorArrivals > 0);
        Assert.Null(host.Session.NavigationTargetFor(TinyFarmIds.Elias));
    }

    [Fact]
    public void RestBeginsOnPhysicalBedArrivalAndDepartureUsesLocomotion()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState tired = TinyFarmM14ControlStates.Create(definitions, "low-energy");
        MovePlayer(tired, definitions, TinyFarmSceneIds.Residence, new SceneAnchorId("residence.from-farm"));
        MoveActor(tired, definitions, TinyFarmIds.Elias, TinyFarmSceneIds.Residence, new SceneAnchorId("residence.from-farm"));
        tired.Minute = 1200;
        var arrival = new TinyFarmSimulationHost(
            new TinyFarmSession(tired, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        arrival.Session.Step(new LookIntent());
        for (int second = 0; second < 20 && !arrival.Session.State.EnergyFor(TinyFarmIds.Elias).IsResting; second++)
        {
            arrival.AdvanceHostTime(TimeSpan.FromSeconds(1));
        }
        Assert.True(arrival.Session.State.EnergyFor(TinyFarmIds.Elias).IsResting);
        Assert.True(arrival.AnchorArrivals > 0);

        TinyFarmState recovered = TinyFarmM14ControlStates.Create(definitions, "wander");
        MovePlayer(recovered, definitions, TinyFarmSceneIds.Residence, new SceneAnchorId("residence.from-farm"));
        MoveActor(recovered, definitions, TinyFarmIds.Elias, TinyFarmSceneIds.Residence, TinyFarmAnchorIds.EliasHomeBed);
        recovered.Minute = 1200;
        int energyIndex = recovered.MutableActorEnergy.FindIndex(item => item.Actor == TinyFarmIds.Elias);
        recovered.MutableActorEnergy[energyIndex] = new ActorEnergyState(TinyFarmIds.Elias, 9_000, true);
        var departure = new TinyFarmSimulationHost(
            new TinyFarmSession(recovered, definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        departure.Session.Step(new LookIntent());
        ScenePosition bed = EliasPosition(departure);
        departure.AdvanceHostTime(TimeSpan.FromMilliseconds(100));
        Assert.NotEqual(bed, EliasPosition(departure));
        Assert.False(departure.Session.State.EnergyFor(TinyFarmIds.Elias).IsResting);
    }

    [Fact]
    public void ActiveInactiveHandoffDiscardsAndDeterministicallyRebuildsWanderPath()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmSimulationHost host = CreateWalkingHost(definitions);
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
        int queriesBeforeHandoff = host.Session.NavigationPlanCount;
        MovePlayer(host.Session.State, definitions, TinyFarmSceneIds.Residence, new SceneAnchorId("residence.from-farm"));
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(100));
        Assert.False(host.Session.HasActiveNpcNavigation);
        MovePlayer(host.Session.State, definitions, TinyFarmSceneIds.Farm, new SceneAnchorId("farm.start"));
        host.Session.Step(new LookIntent());
        Assert.True(host.Session.HasActiveNpcNavigation);
        Assert.True(host.Session.NavigationPlanCount > queriesBeforeHandoff);
    }

    [Fact]
    public void CanonicalScenarioProducesOutcomeA()
    {
        TinyFarmM14Evidence evidence = TinyFarmM14Scenario.Prove();
        string json = TinyFarmM14Scenario.WriteJson(evidence.Proof);
        Assert.Contains("\"outcome\": \"A\"", json);
    }

    private static TinyFarmSimulationHost CreateWalkingHost(TinyFarmDefinitions? definitions = null)
    {
        definitions ??= TinyFarmDefinitionLoader.LoadM14();
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM14ControlStates.Create(definitions, "wander"), definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        host.Session.Step(new LookIntent());
        Assert.True(TinyFarmAnchorIds.IsWander(
            host.Session.NavigationTargetFor(TinyFarmIds.Elias)!.Value));
        return host;
    }

    private static TinyFarmScheduleDecision Decide(
        TinyFarmDefinitions definitions,
        SceneAnchorId current,
        int energy,
        int minute)
    {
        return TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Elias,
            minute,
            current,
            includeTrace: true,
            energy);
    }

    private static ScenePosition EliasPosition(TinyFarmSimulationHost host)
    {
        return host.Session.State.ActorScene(TinyFarmIds.Elias).WorldPosition;
    }

    private static void MovePlayer(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        SceneId scene,
        SceneAnchorId anchor)
    {
        MoveActor(state, definitions, TinyFarmIds.Player, scene, anchor);
    }

    private static void MoveActor(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        ActorId actor,
        SceneId scene,
        SceneAnchorId anchor)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[index] = new ActorSceneState(
            actor,
            scene,
            definitions.Scenes.GetAnchor(anchor).Position);
    }

    private static void Advance(TinyFarmSimulationHost host, TimeSpan total, int partitions)
    {
        long quotient = total.Ticks / partitions;
        long remainder = total.Ticks % partitions;
        for (int index = 0; index < partitions; index++)
        {
            host.AdvanceHostTime(TimeSpan.FromTicks(quotient + (index < remainder ? 1 : 0)));
        }
    }

    private static void AssertEquivalent(TinyFarmSimulationHost left, TinyFarmSimulationHost right)
    {
        Assert.Equal(TinyFarmSemanticHash.Compute(left.Session.State), TinyFarmSemanticHash.Compute(right.Session.State));
        Assert.Equal(EliasPosition(left), EliasPosition(right));
        Assert.Equal(
            left.Session.State.ActorScene(TinyFarmIds.Elias).Facing,
            right.Session.State.ActorScene(TinyFarmIds.Elias).Facing);
        Assert.Equal(left.Session.WaypointIndexFor(TinyFarmIds.Elias), right.Session.WaypointIndexFor(TinyFarmIds.Elias));
        Assert.Equal(left.Session.NavigationTargetFor(TinyFarmIds.Elias), right.Session.NavigationTargetFor(TinyFarmIds.Elias));
        Assert.Equal(left.LocomotionStepsAdvanced, right.LocomotionStepsAdvanced);
        Assert.Equal(left.Session.DecisionEvaluationCount, right.Session.DecisionEvaluationCount);
        Assert.Equal(left.Session.NavigationPlanCount, right.Session.NavigationPlanCount);
    }
}
