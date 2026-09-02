using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM12Tests
{
    [Fact]
    public void EveryNpcHasOneDistinctOwnedReachableBedAndStructuralBedtime()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        ActorId[] actors = [TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela];
        SceneAnchorId[] beds = actors.Select(TinyFarmAnchorIds.HomeBedFor).ToArray();

        Assert.Equal(3, beds.Distinct().Count());
        foreach ((ActorId actor, SceneAnchorId bedId) in actors.Zip(beds))
        {
            SceneAnchorDefinition bed = definitions.Scenes.GetAnchor(bedId);
            SceneObjectDefinition bedObject = definitions.Scenes.Get(bed.Scene).Object(bed.SemanticObject!.Value);
            Assert.Equal(TinyFarmSceneIds.Residence, bed.Scene);
            Assert.Equal(SceneAnchorKind.Rest, bed.Kind);
            Assert.Equal(SceneObjectKind.Bed, bedObject.Kind);
            Assert.Equal(actor.Value, bedObject.SemanticReference);
            Assert.Contains(definitions.Schedules.Windows, window =>
                window.Actor == actor
                && window.Regime == TinyFarmScheduleRegime.Required
                && window.RequiredAnchor == bedId);
        }
    }

    [Fact]
    public void EnergyLawIsBoundedDeterministicAndTimePartitionIndependent()
    {
        Assert.Equal(8_760, TinyFarmEnergy.Advance(TinyFarmEnergy.InitialUnits, false, 30));
        Assert.Equal(10_000, TinyFarmEnergy.Advance(9_000, true, 30));
        Assert.Equal(0, TinyFarmEnergy.Advance(100, false, 30));

        int whole = TinyFarmEnergy.Advance(7_000, false, 30);
        int partitioned = TinyFarmEnergy.Advance(
            TinyFarmEnergy.Advance(7_000, false, 10),
            false,
            20);
        Assert.Equal(whole, partitioned);
    }

    [Fact]
    public void OpenEnergyUtilityIsMonotonicAndLowEnergySelectsPersonalBed()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmScheduleDecision high = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true,
            energy: 9_000);
        TinyFarmScheduleDecision low = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true,
            energy: 1_000);

        Assert.Equal(TinyFarmAnchorIds.TownSquare, high.SelectedAnchor);
        Assert.Equal(TinyFarmAnchorIds.MaraHomeBed, low.SelectedAnchor);
        TinyFarmUtilityScore highRest = high.UtilityScores.Single(score => score.Candidate == TinyFarmAnchorIds.MaraHomeBed);
        TinyFarmUtilityScore lowRest = low.UtilityScores.Single(score => score.Candidate == TinyFarmAnchorIds.MaraHomeBed);
        Assert.True(lowRest.EnergyContribution > highRest.EnergyContribution);
        Assert.Equal(0.1d, lowRest.BaseScore);
        Assert.Equal(lowRest.BaseScore + lowRest.StickinessContribution + lowRest.EnergyContribution, lowRest.Score);
    }

    [Fact]
    public void RequiredBedtimeStructurallyOverridesHighEnergyAndSkipsUtility()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmScheduleDecision bedtime = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1320,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true,
            energy: TinyFarmEnergy.MaximumUnits);

        Assert.Equal(TinyFarmScheduleRegime.Required, bedtime.Regime);
        Assert.Equal(TinyFarmAnchorIds.MaraHomeBed, bedtime.SelectedAnchor);
        Assert.Empty(bedtime.UtilityScores);
    }

    [Fact]
    public void ActiveNpcWalksToPersonalBedThenRecoversEnergy()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmState state = LowEnergyMaraInResidence(definitions);
        var session = new TinyFarmSession(state, definitions);

        for (int step = 0; step < 80 && !session.State.EnergyFor(TinyFarmIds.Mara).IsResting; step++)
        {
            session.Step(new WaitIntent(1));
        }

        Assert.True(session.NavigationPlanCount > 0);
        Assert.True(session.State.EnergyFor(TinyFarmIds.Mara).IsResting);
        int energyAtArrival = session.State.EnergyFor(TinyFarmIds.Mara).Energy;
        session.Step(new WaitIntent(10));
        Assert.True(session.State.EnergyFor(TinyFarmIds.Mara).Energy > energyAtArrival);
        Assert.Equal(TinyFarmAnchorIds.MaraHomeBed, TinyFarmNpcController.CurrentAnchor(
            session.State,
            session.State.Actor(TinyFarmIds.Mara),
            definitions.Scenes,
            definitions.Schedules));
    }

    [Fact]
    public void InactiveNpcUsesNoPathAndSaveLoadPreservesLowAndRestingEnergy()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        state.Minute = 1200;
        SetEnergy(state, TinyFarmIds.Mara, 1_000, false);
        ScenePosition residenceSpawn = definitions.Scenes.GetAnchor(new SceneAnchorId("residence.from-farm")).Position;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Residence, residenceSpawn);
        SetLocation(state, TinyFarmIds.Player, TinyFarmIds.Farmhouse);
        var session = new TinyFarmSession(state, definitions);

        session.Step(new WaitIntent(1));
        Assert.Equal(0, session.NavigationPlanCount);
        byte[] lowSave = session.CaptureWeekSave();
        TinyFarmSession lowReloaded = TinyFarmChunkedSaveCodec.Read(lowSave, definitions);
        Assert.Equal(session.State.EnergyFor(TinyFarmIds.Mara), lowReloaded.State.EnergyFor(TinyFarmIds.Mara));
        Assert.Equal(TinyFarmSemanticHash.Compute(session.State), TinyFarmSemanticHash.Compute(lowReloaded.State));

        for (int step = 0; step < 5 && !session.State.EnergyFor(TinyFarmIds.Mara).IsResting; step++)
        {
            session.Step(new WaitIntent(1));
        }
        Assert.True(session.State.EnergyFor(TinyFarmIds.Mara).IsResting);
        byte[] restingSave = session.CaptureWeekSave();
        TinyFarmSession restingReloaded = TinyFarmChunkedSaveCodec.Read(restingSave, definitions);
        int before = restingReloaded.State.EnergyFor(TinyFarmIds.Mara).Energy;
        restingReloaded.Step(new WaitIntent(10));
        Assert.True(restingReloaded.State.EnergyFor(TinyFarmIds.Mara).Energy > before);
    }

    [Fact]
    public void M12InspectionExposesEnergyRegimeGoalRestStateAndTrace()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        state.Minute = 1200;
        SetEnergy(state, TinyFarmIds.Mara, 1_800, false);
        var session = new TinyFarmSession(state, definitions);

        string json = TinyFarmInspector.WriteJson(session, []);
        Assert.Contains("\"energy\": 1800", json);
        Assert.Contains("\"regime\": \"Open\"", json);
        Assert.Contains("\"selectedAnchor\": {", json);
        Assert.Contains("\"energyContribution\"", json);
    }

    [Fact]
    public void CandidateLookupAndEnergyScorerAllocateNothingAtSteadyState()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmScheduleWindow window = definitions.Schedules.Windows.Single(item => item.Id == "mara.free-evening");
        _ = definitions.Schedules.CandidatesFor(window);
        _ = TinyFarmEnergy.RestContribution(1_800);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 10_000; index++)
        {
            _ = definitions.Schedules.CandidatesFor(window);
            _ = TinyFarmEnergy.RestContribution(index % TinyFarmEnergy.MaximumUnits);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TiredAndRestingHandoffsPreserveSemanticContinuation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmState tired = LowEnergyMaraInResidence(definitions);
        var active = new TinyFarmSession(tired, definitions);
        active.Step(new WaitIntent(1));
        int tiredEnergy = active.State.EnergyFor(TinyFarmIds.Mara).Energy;

        TinyFarmState inactiveState = active.State.DeepCopy();
        ScenePosition town = definitions.Scenes.GetAnchor(TinyFarmAnchorIds.TownSquare).Position;
        SetPlacement(inactiveState, TinyFarmIds.Player, TinyFarmSceneIds.Town, town);
        SetLocation(inactiveState, TinyFarmIds.Player, TinyFarmIds.TownSquare);
        var inactive = new TinyFarmSession(inactiveState, definitions);
        for (int step = 0; step < 4 && !inactive.State.EnergyFor(TinyFarmIds.Mara).IsResting; step++)
        {
            inactive.Step(new WaitIntent(1));
        }
        Assert.True(inactive.State.EnergyFor(TinyFarmIds.Mara).IsResting);
        Assert.True(inactive.State.EnergyFor(TinyFarmIds.Mara).Energy <= tiredEnergy);
        int restingEnergy = inactive.State.EnergyFor(TinyFarmIds.Mara).Energy;

        TinyFarmState returnedState = inactive.State.DeepCopy();
        ScenePosition entry = definitions.Scenes.GetAnchor(new SceneAnchorId("residence.from-farm")).Position;
        SetPlacement(returnedState, TinyFarmIds.Player, TinyFarmSceneIds.Residence, entry);
        SetLocation(returnedState, TinyFarmIds.Player, TinyFarmIds.Farmhouse);
        var returned = new TinyFarmSession(returnedState, definitions);
        returned.Step(new WaitIntent(10));
        Assert.True(returned.State.EnergyFor(TinyFarmIds.Mara).IsResting);
        Assert.True(returned.State.EnergyFor(TinyFarmIds.Mara).Energy > restingEnergy);
    }

    [Fact]
    public void RestingNpcLeavesBedWhenOpenUtilitySelectsAnotherGoal()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmState state = LowEnergyMaraInResidence(definitions);
        ScenePosition bed = definitions.Scenes.GetAnchor(TinyFarmAnchorIds.MaraHomeBed).Position;
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Residence, bed);
        SetEnergy(state, TinyFarmIds.Mara, TinyFarmEnergy.MaximumUnits, true);
        var session = new TinyFarmSession(state, definitions);

        session.Step(new WaitIntent(1));
        Assert.False(session.State.EnergyFor(TinyFarmIds.Mara).IsResting);
    }

    [Fact]
    public void InvalidEnergyStateIsRejectedBeforeSave()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        SetEnergy(state, TinyFarmIds.Mara, TinyFarmEnergy.MaximumUnits + 1, false);
        var session = new TinyFarmSession(state, definitions);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => session.CaptureWeekSave());
        Assert.Contains("finite, bounded", exception.Message);
    }

    [Theory]
    [InlineData("high-open", 9_000, 1200, "town.square")]
    [InlineData("low-open", 1_000, 1200, "mara.home-bed")]
    [InlineData("bedtime", 10_000, 1320, "mara.home-bed")]
    [InlineData("resting", 1_000, 1200, "mara.home-bed")]
    public void CliControlPhasesCreateInspectableCanonicalStates(
        string phase,
        int expectedEnergy,
        int expectedMinute,
        string expectedAnchor)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmState state = TinyFarmM12ControlStates.Create(definitions, phase);
        TinyFarmScheduleDecision decision = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            state.Minute,
            TinyFarmNpcController.CurrentAnchor(
                state,
                state.Actor(TinyFarmIds.Mara),
                definitions.Scenes,
                definitions.Schedules),
            energy: state.EnergyFor(TinyFarmIds.Mara).Energy);

        Assert.Equal(expectedEnergy, state.EnergyFor(TinyFarmIds.Mara).Energy);
        Assert.Equal(expectedMinute, state.Minute);
        Assert.Equal(expectedAnchor, decision.SelectedAnchor.Value);
    }

    private static TinyFarmState LowEnergyMaraInResidence(TinyFarmDefinitions definitions)
    {
        TinyFarmState state = TinyFarmContent.CreateEnergySceneState(definitions);
        state.Minute = 1200;
        ScenePosition spawn = definitions.Scenes.GetAnchor(new SceneAnchorId("residence.from-farm")).Position;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Residence, spawn);
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Residence, spawn);
        SetLocation(state, TinyFarmIds.Player, TinyFarmIds.Farmhouse);
        SetLocation(state, TinyFarmIds.Mara, TinyFarmIds.Farmhouse);
        SetEnergy(state, TinyFarmIds.Mara, 1_000, false);
        return state;
    }

    private static void SetEnergy(TinyFarmState state, ActorId actor, int energy, bool isResting)
    {
        int index = state.MutableActorEnergy.FindIndex(item => item.Actor == actor);
        state.MutableActorEnergy[index] = new ActorEnergyState(actor, energy, isResting);
    }

    private static void SetPlacement(TinyFarmState state, ActorId actor, SceneId scene, ScenePosition position)
    {
        int index = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[index] = new ActorSceneState(actor, scene, position);
    }

    private static void SetLocation(TinyFarmState state, ActorId actor, LocationId location)
    {
        int index = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[index] = state.MutableActors[index] with { Location = location };
    }
}
