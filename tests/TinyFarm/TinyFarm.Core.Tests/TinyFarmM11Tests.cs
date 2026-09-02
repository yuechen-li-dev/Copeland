using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM11Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void IndexedCandidateLookupHasZeroSteadyStateManagedAllocation()
    {
        TinyFarmScheduleWindow open = OpenWindow(definitions.Schedules);
        for (int index = 0; index < 10_000; index++)
        {
            _ = definitions.Schedules.CandidatesFor(open);
        }

        const int count = 100_000;
        int observedCandidates = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++)
        {
            observedCandidates += definitions.Schedules.CandidatesFor(open).Count;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(count * 2, observedCandidates);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void OrdinaryOpenDecisionStaysWithinTinyFarmLocalAllocationBudget()
    {
        for (int index = 0; index < 10_000; index++)
        {
            _ = DecideOpen(definitions.Schedules);
        }

        const int count = 10_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++)
        {
            _ = DecideOpen(definitions.Schedules);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        double bytesPerDecision = allocated / (double)count;

        Assert.InRange(bytesPerDecision, 0d, 640d);
    }

    [Fact]
    public void ScoreTraceIsOptInAndPreservesM10InspectionShape()
    {
        TinyFarmScheduleDecision ordinary = DecideOpen(definitions.Schedules);
        TinyFarmScheduleDecision inspected = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true);

        Assert.Empty(ordinary.UtilityScores);
        Assert.Equal(2, inspected.UtilityScores.Count);
        Assert.Single(inspected.UtilityScores, score => score.Selected);
        Assert.Equal(TinyFarmAnchorIds.TownSquare, inspected.SelectedAnchor);
    }

    [Fact]
    public void RepeatedInterleavedActorsKeepIndependentPersistentChoices()
    {
        TinyFarmScheduleCatalog catalog = CreateInterleaveCatalog();
        ActorId[] actors = [TinyFarmIds.Mara, TinyFarmIds.Elias, TinyFarmIds.Sela];
        SceneAnchorId[] currentAnchors =
        [
            TinyFarmAnchorIds.TownSquare,
            TinyFarmAnchorIds.FarmHome,
            TinyFarmAnchorIds.TownSquare
        ];

        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            int actorIndex = iteration % actors.Length;
            TinyFarmScheduleDecision decision = TinyFarmNpcSchedule.Decide(
                catalog,
                actors[actorIndex],
                1200,
                currentAnchors[actorIndex]);
            Assert.Equal(currentAnchors[actorIndex], decision.SelectedAnchor);
        }
    }

    [Fact]
    public void CandidateRowReorderRetainsCanonicalIndexTraceAndWinner()
    {
        var reordered = new TinyFarmScheduleCatalog(
            definitions.Schedules.Windows,
            definitions.Schedules.Candidates.Reverse());

        TinyFarmScheduleDecision expected = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true);
        TinyFarmScheduleDecision actual = TinyFarmNpcSchedule.Decide(
            reordered,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true);

        Assert.Equal(expected.SelectedAnchor, actual.SelectedAnchor);
        Assert.Equal(expected.UtilityScores, actual.UtilityScores);
        Assert.Equal(definitions.Schedules.Candidates, reordered.Candidates);
    }

    [Fact]
    public void RepeatedSessionReplacementRebuildsFromSemanticStateWithoutStaleChoice()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 1200;
        var session = new TinyFarmSession(state, definitions);
        byte[] save = session.CaptureWeekSave();

        for (int iteration = 0; iteration < 25; iteration++)
        {
            session = TinyFarmChunkedSaveCodec.Read(save, definitions);
            TinyFarmStepResult step = session.Step(new LookIntent());
            Assert.Equal(1200, step.State.Minute);
            Assert.Equal(
                TinyFarmAnchorIds.FarmHome,
                TinyFarmNpcSchedule.Decide(
                    definitions.Schedules,
                    TinyFarmIds.Mara,
                    1200,
                    TinyFarmAnchorIds.FarmHome).SelectedAnchor);
        }
    }

    [Fact]
    public void MissingIndexedCandidatesStillFailsClearly()
    {
        TinyFarmScheduleWindow source = OpenWindow(definitions.Schedules);
        var catalog = new TinyFarmScheduleCatalog([source], []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            TinyFarmNpcSchedule.Decide(catalog, TinyFarmIds.Mara, 1200));

        Assert.Contains("no indexed utility candidates", exception.Message, StringComparison.Ordinal);
    }

    private static TinyFarmScheduleDecision DecideOpen(TinyFarmScheduleCatalog catalog)
    {
        return TinyFarmNpcSchedule.Decide(
            catalog,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare);
    }

    private static TinyFarmScheduleWindow OpenWindow(TinyFarmScheduleCatalog catalog)
    {
        return catalog.Windows.Single(window => window.Regime == TinyFarmScheduleRegime.Open);
    }

    private static TinyFarmScheduleCatalog CreateInterleaveCatalog()
    {
        ActorId[] actors = [TinyFarmIds.Mara, TinyFarmIds.Elias, TinyFarmIds.Sela];
        TinyFarmScheduleWindow[] windows = actors.Select(actor => new TinyFarmScheduleWindow(
            $"{actor.Value}.open",
            actor,
            TinyFarmScheduleDay.EveryDay,
            0,
            1440,
            TinyFarmScheduleRegime.Open,
            null,
            0,
            "interleave isolation proof")).ToArray();
        TinyFarmUtilityCandidate[] candidates = windows.SelectMany(window => new[]
        {
            new TinyFarmUtilityCandidate(
                window.Id,
                TinyFarmAnchorIds.FarmHome,
                "current-location-stickiness",
                0.6d,
                0.2d),
            new TinyFarmUtilityCandidate(
                window.Id,
                TinyFarmAnchorIds.TownSquare,
                "current-location-stickiness",
                0.6d,
                0.2d)
        }).ToArray();
        return new TinyFarmScheduleCatalog(windows, candidates);
    }
}
