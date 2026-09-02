using Copeland.TS.Tson;
using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM13Tests
{
    [Fact]
    public void PausedFreezesSemanticStateAndResumeHasNoTimeJump()
    {
        TinyFarmSimulationHost host = CreateHost();
        string initial = TinyFarmSemanticHash.Compute(host.Session.State);

        host.AdvanceHostTime(TimeSpan.FromMinutes(10));
        Assert.Equal(initial, TinyFarmSemanticHash.Compute(host.Session.State));
        Assert.Equal(0, host.WorldMinutesAdvanced);

        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        Assert.Equal(481, host.Session.State.Minute);
        Assert.Equal(1, host.WorldMinutesAdvanced);
    }

    [Fact]
    public void PlayAndFastForwardUseExactTypedRates()
    {
        TinyFarmSimulationHost host = CreateHost();
        host.Execute(TinyFarmSimulationCommandParser.Parse("play"));
        TinyFarmHostAdvanceResult play = host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        Assert.Equal(1, play.WorldMinutesAdvanced);
        Assert.Equal(300, play.LocomotionStepsAdvanced);

        host.Execute(TinyFarmSimulationCommandParser.Parse("fast-forward"));
        TinyFarmHostAdvanceResult fast = host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        Assert.Equal(10, fast.WorldMinutesAdvanced);
        Assert.Equal(3_000, fast.LocomotionStepsAdvanced);
        Assert.Equal(491, host.Session.State.Minute);
    }

    [Fact]
    public void SixtyAndOneHundredFortyFourHertzPartitionsAreEquivalent()
    {
        TinyFarmSimulationHost sixty = CreatePlayingHost();
        TinyFarmSimulationHost oneFortyFour = CreatePlayingHost();

        AdvancePartitioned(sixty, TimeSpan.FromSeconds(60), 60 * 60);
        AdvancePartitioned(oneFortyFour, TimeSpan.FromSeconds(60), 60 * 144);

        AssertEquivalent(sixty, oneFortyFour);
        Assert.Equal(12, sixty.WorldMinutesAdvanced);
        Assert.Equal(3_600, sixty.LocomotionStepsAdvanced);
    }

    [Fact]
    public void IrregularHostDeltasMatchEvenPartition()
    {
        TinyFarmSimulationHost irregular = CreatePlayingHost();
        TinyFarmSimulationHost even = CreatePlayingHost();
        int[] pattern = [16, 16, 50, 3, 91, 7, 33, 84];
        long totalTicks = 0;
        for (int repeat = 0; repeat < 25; repeat++)
        {
            foreach (int milliseconds in pattern)
            {
                TimeSpan delta = TimeSpan.FromMilliseconds(milliseconds);
                irregular.AdvanceHostTime(delta);
                totalTicks += delta.Ticks;
            }
        }
        AdvancePartitioned(even, TimeSpan.FromTicks(totalTicks), 317);
        AssertEquivalent(irregular, even);
    }

    [Fact]
    public void LongRunIntegerAccumulatorHasNoClockDrift()
    {
        TinyFarmSimulationHost host = CreatePlayingHost();
        for (int second = 0; second < 1_000; second++)
        {
            host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(200, host.WorldMinutesAdvanced);
        Assert.Equal(60_000, host.LocomotionStepsAdvanced);
    }

    [Fact]
    public void CatchUpClampDiscardsExcessWithoutBacklog()
    {
        TinyFarmSimulationHost host = CreatePlayingHost();
        TinyFarmHostAdvanceResult result = host.AdvanceHostTime(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, result.HostTicksAccepted);
        Assert.Equal(TimeSpan.FromSeconds(25).Ticks, result.HostTicksDiscarded);
        Assert.Equal(1, result.WorldMinutesAdvanced);
    }

    [Fact]
    public void WorldMinutesDriveEnergyAndDecisionsNotRenderFrames()
    {
        TinyFarmSimulationHost host = CreatePlayingHost();
        int initialEnergy = host.Session.State.EnergyFor(TinyFarmIds.Mara).Energy;
        for (int frame = 0; frame < 3_600; frame++)
        {
            host.ObserveRenderFrame();
        }
        Assert.Equal(initialEnergy, host.Session.State.EnergyFor(TinyFarmIds.Mara).Energy);
        Assert.Equal(0, host.Session.DecisionEvaluationCount);

        AdvancePartitioned(host, TimeSpan.FromSeconds(60), 60);
        Assert.Equal(initialEnergy - (12 * 8), host.Session.State.EnergyFor(TinyFarmIds.Mara).Energy);
        Assert.Equal(12 * 3, host.Session.DecisionEvaluationCount);
        Assert.True(host.Session.NavigationPlanCount < host.RenderFramesObserved);
    }

    [Fact]
    public void ModeSwitchSequenceIsDeterministic()
    {
        TinyFarmSimulationHost first = CreateHost();
        TinyFarmSimulationHost second = CreateHost();
        foreach (TinyFarmSimulationHost host in new[] { first, second })
        {
            host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
            host.AdvanceHostTime(TimeSpan.FromSeconds(5));
            host.AdvanceHostTime(TimeSpan.FromSeconds(5));
            host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.FastForward));
            host.AdvanceHostTime(TimeSpan.FromSeconds(2));
            host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Paused));
            host.AdvanceHostTime(TimeSpan.FromSeconds(5));
            host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
            host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        }
        AssertEquivalent(first, second);
        Assert.Equal(7, first.WorldMinutesAdvanced);
    }

    [Fact]
    public void SaveLoadReplacementResetsFractionalAccumulators()
    {
        TinyFarmSimulationHost host = CreatePlayingHost();
        host.AdvanceHostTime(TimeSpan.FromSeconds(4));
        byte[] save = host.Session.CaptureWeekSave();
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        host.ReplaceSession(TinyFarmChunkedSaveCodec.Read(save, definitions));

        host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        Assert.Equal(480, host.Session.State.Minute);
        host.AdvanceHostTime(TimeSpan.FromSeconds(4));
        Assert.Equal(481, host.Session.State.Minute);
    }

    [Fact]
    public void CanonicalTsonSnapshotIsStableParseableAndRendererFree()
    {
        TinyFarmSimulationHost host = CreatePlayingHost();
        host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        TinyFarmSimulationSnapshot snapshot = host.Snapshot();
        string first = TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(snapshot);
        string second = TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(host.Snapshot());

        Assert.Equal(first, second);
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(first, TsonDocumentProfile.CanonicalTson);
        string diagnostics = string.Join("; ", read.SyntaxDiagnostics
            .Select(item => item.ToString())
            .Concat(read.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        Assert.True(read.Success, diagnostics);
        Assert.DoesNotContain("Vector2", first);
        Assert.DoesNotContain("Texture", first);
        Assert.Contains("tiny-farm-simulation@1", first);
    }

    [Fact]
    public void ExplicitAdvanceIsHeadlessAndIndependentOfMode()
    {
        TinyFarmSimulationHost host = CreateHost();
        host.Execute(TinyFarmSimulationCommandParser.Parse("advance 30"));
        Assert.Equal(TinyFarmSimulationMode.Paused, host.Mode);
        Assert.Equal(510, host.Session.State.Minute);
        Assert.Equal(30, host.WorldMinutesAdvanced);
    }

    [Fact]
    public void PlayerLocomotionDoesNotEvaluateNpcPolicyAtRenderCadence()
    {
        TinyFarmSimulationHost host = CreatePlayingHost();
        host.SetPlayerMovement(1, 0);
        TinyFarmHostAdvanceResult movement = host.AdvanceHostTime(TimeSpan.FromSeconds(1));
        Assert.Equal(60, movement.LocomotionStepsAdvanced);
        Assert.Equal(60, movement.Results.Count(result =>
            result.Envelope.Source == IntentSourceKind.Human
            && result.Envelope.Intent is SpatialMoveIntent));
        Assert.Equal(0, host.Session.DecisionEvaluationCount);
        host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        Assert.Equal(3, host.Session.DecisionEvaluationCount);
    }

    [Fact]
    public void PersonalBedTieUsesTheActualSharedDominatusOptionOrder()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmScheduleDecision decision = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.MaraHomeBed,
            energy: 5_000);
        Assert.Equal(TinyFarmAnchorIds.MaraHomeBed, decision.SelectedAnchor);
    }

    [Fact]
    public void HostedWorldMinutesDriveExactScheduleAndCropDayBoundaries()
    {
        TinyFarmSimulationHost host = CreateHost();
        host.Execute(new AdvanceMinutesCommand(840));
        TinyFarmSimulationActorSnapshot mara = host.Snapshot().Actors.Single(actor => actor.Id == TinyFarmIds.Mara);
        Assert.Equal(1320, host.Session.State.Minute);
        Assert.Equal(TinyFarmScheduleRegime.Required, mara.Regime);
        Assert.Equal(TinyFarmAnchorIds.MaraHomeBed, mara.Goal);

        TinyFarmDefinitions weekDefinitions = TinyFarmDefinitionLoader.Load();
        TinyFarmState cropState = TinyFarmContent.CreateWeekState(weekDefinitions);
        cropState.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 1));
        var cropHost = new TinyFarmSimulationHost(
            new TinyFarmSession(cropState, weekDefinitions),
            weekDefinitions);
        cropHost.ExecuteIntent(new MoveIntent(TinyFarmIds.Farmhouse));
        IntentResult planted = cropHost.ExecuteIntent(
            new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop)).Results
            .Single(result => result.Envelope.Source == IntentSourceKind.Human);
        IntentResult watered = cropHost.ExecuteIntent(new WaterIntent(TinyFarmIds.PlotOne)).Results
            .Single(result => result.Envelope.Source == IntentSourceKind.Human);
        Assert.Equal(IntentResultStatus.Accepted, planted.Status);
        Assert.Equal(IntentResultStatus.Accepted, watered.Status);

        cropHost.Execute(new AdvanceMinutesCommand(960));
        FarmPlotState plot = cropHost.Session.State.FarmPlots.Single(item => item.Id == TinyFarmIds.PlotOne);
        Assert.Equal(2, cropHost.Session.State.Day);
        Assert.Equal(1, plot.GrowthStage);
    }

    [Fact]
    public void PausedPerFrameHostAdvanceAllocatesNothingAtSteadyState()
    {
        TinyFarmSimulationHost host = CreateHost();
        _ = host.AdvanceHostTime(TimeSpan.FromMilliseconds(16));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 10_000; frame++)
        {
            _ = host.AdvanceHostTime(TimeSpan.FromMilliseconds(16));
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static TinyFarmSimulationHost CreateHost()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        return new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmContent.CreateEnergySceneState(definitions), definitions),
            definitions);
    }

    private static TinyFarmSimulationHost CreatePlayingHost()
    {
        TinyFarmSimulationHost host = CreateHost();
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        return host;
    }

    private static void AdvancePartitioned(TinyFarmSimulationHost host, TimeSpan total, int partitions)
    {
        long quotient = total.Ticks / partitions;
        long remainder = total.Ticks % partitions;
        for (int index = 0; index < partitions; index++)
        {
            host.AdvanceHostTime(TimeSpan.FromTicks(quotient + (index < remainder ? 1 : 0)));
        }
    }

    private static void AssertEquivalent(TinyFarmSimulationHost first, TinyFarmSimulationHost second)
    {
        Assert.Equal(first.Session.State.Day, second.Session.State.Day);
        Assert.Equal(first.Session.State.Minute, second.Session.State.Minute);
        Assert.Equal(TinyFarmSemanticHash.Compute(first.Session.State), TinyFarmSemanticHash.Compute(second.Session.State));
        Assert.Equal(first.WorldMinutesAdvanced, second.WorldMinutesAdvanced);
        Assert.Equal(first.LocomotionStepsAdvanced, second.LocomotionStepsAdvanced);
        Assert.Equal(first.Session.DecisionEvaluationCount, second.Session.DecisionEvaluationCount);
        Assert.Equal(first.Session.NavigationPlanCount, second.Session.NavigationPlanCount);
    }
}
