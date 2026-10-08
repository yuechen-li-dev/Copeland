using Copeland.TS.Tson;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM10Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void PayloadEnumProductionScheduleLoadsWithoutFlatDayTokens()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "tiny-farm-npc-schedules.obj.ts"));

        Assert.Contains("day: ScheduleDay", source, StringComparison.Ordinal);
        Assert.Contains("ScheduleDay.Day(6)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Day6", source, StringComparison.Ordinal);
        Assert.Equal(12, definitions.Schedules.Windows.Count);
    }

    [Fact]
    public void PayloadEnumCanonicalRoundtripRetainsCaseAndPayload()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "tiny-farm-npc-schedules.obj.ts"));
        TsonReadResult first = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript);
        Assert.True(first.Success);
        string canonical = TsonCanonicalPrinter.Print(first.Document!);
        TsonReadResult second = TsonDocumentReader.ReadSelfDescribed(
            canonical,
            TsonDocumentProfile.CanonicalTson);
        Assert.True(second.Success);
        TsonTable table = Assert.IsType<TsonTable>(second.Document!.Root);
        TsonEnum daySix = Assert.IsType<TsonEnum>(table.Columns.Single(column => column.Schema.Name == "day").Cells[4]);
        Assert.Equal("Day", daySix.CaseName);
        Assert.Equal(6d, Assert.IsType<TsonNumber>(Assert.Single(daySix.Payloads).Value).Value);
    }

    [Fact]
    public void PayloadMigrationAndAllRequiredDefaultPreserveAll30240M9Anchors()
    {
        int compared = 0;
        foreach (ActorId actor in Npcs())
        {
            for (int minute = 0; minute < 7 * 1440; minute++)
            {
                Assert.Equal(LegacyAnchor(actor, minute), TinyFarmNpcSchedule.Decide(
                    definitions.Schedules,
                    actor,
                    minute).SelectedAnchor);
                compared++;
            }
        }

        Assert.Equal(30240, compared);
    }

    [Fact]
    public void RequiredSkipsUtilityWhileOpenIsBoundedDeterministicAndStateSensitive()
    {
        TinyFarmScheduleDecision required = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1320,
            TinyFarmAnchorIds.TownSquare);
        TinyFarmScheduleDecision openAtHome = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.FarmHome,
            includeTrace: true);
        TinyFarmScheduleDecision openAtTown = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true);
        TinyFarmScheduleDecision repeated = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace: true);

        Assert.Equal(TinyFarmScheduleRegime.Required, required.Regime);
        Assert.Equal(TinyFarmAnchorIds.FarmHome, required.SelectedAnchor);
        Assert.Empty(required.UtilityScores);
        Assert.Equal(TinyFarmAnchorIds.FarmHome, openAtHome.SelectedAnchor);
        Assert.Equal(TinyFarmAnchorIds.TownSquare, openAtTown.SelectedAnchor);
        Assert.Equal(openAtTown.SelectedAnchor, repeated.SelectedAnchor);
        Assert.Equal(openAtTown.UtilityScores, repeated.UtilityScores);
        Assert.Equal(2, openAtTown.UtilityScores.Count);
        Assert.DoesNotContain(
            openAtTown.UtilityScores,
            score => score.Candidate == TinyFarmAnchorIds.RiversideMeetingPoint);
    }

    [Fact]
    public void ExistingInspectionExposesRegimeWinnerAndOpenScores()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 1200;
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Town, definitions.Scenes.GetAnchor(TinyFarmAnchorIds.TownSquare).Position);
        var session = new TinyFarmSession(state, definitions);

        string json = TinyFarmInspector.WriteJson(session, []);

        Assert.Contains("mara.free-evening", json, StringComparison.Ordinal);
        Assert.Contains("utilityScores", json, StringComparison.Ordinal);
        Assert.Contains("town.square", json, StringComparison.Ordinal);
    }

    [Fact]
    public void HardBoundaryOverridesOpenGoalAndReplansActiveNpc()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 1319;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Town, At(10, 12));
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Town, definitions.Scenes.GetAnchor(TinyFarmAnchorIds.TownSquare).Position);
        var session = new TinyFarmSession(state, definitions);

        Assert.Equal(TinyFarmAnchorIds.TownSquare, TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1319,
            TinyFarmAnchorIds.TownSquare).SelectedAnchor);
        session.Step(new LookIntent());
        int plansBeforeBoundary = session.NavigationPlanCount;
        session.Step(new WaitIntent(1));

        TinyFarmScheduleDecision hard = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1320,
            TinyFarmAnchorIds.TownSquare);
        Assert.Equal(TinyFarmAnchorIds.FarmHome, hard.SelectedAnchor);
        Assert.True(session.NavigationPlanCount > plansBeforeBoundary);
    }

    [Fact]
    public void InactiveAndSaveLoadUseTheSameOpenAndRequiredLawsWithoutPaths()
    {
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        state.Minute = 1319;
        SetPlacement(state, TinyFarmIds.Player, TinyFarmSceneIds.Overworld, At(6, 6));
        SetPlacement(state, TinyFarmIds.Mara, TinyFarmSceneIds.Town, definitions.Scenes.GetAnchor(TinyFarmAnchorIds.TownSquare).Position);
        var original = new TinyFarmSession(state, definitions);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(original.CaptureWeekSave(), definitions);

        original.Step(new LookIntent());
        loaded.Step(new LookIntent());
        Assert.Equal(TinyFarmSemanticHash.Compute(original.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal(0, original.NavigationPlanCount);
        Assert.Equal(0, loaded.NavigationPlanCount);

        original.Step(new WaitIntent(1));
        loaded.Step(new WaitIntent(1));
        Assert.Equal(TinyFarmSemanticHash.Compute(original.State), TinyFarmSemanticHash.Compute(loaded.State));
        Assert.Equal(TinyFarmIds.Farmhouse, original.State.Actor(TinyFarmIds.Mara).Location);
        Assert.Equal(0, original.NavigationPlanCount);
        Assert.Equal(0, loaded.NavigationPlanCount);
    }

    [Fact]
    public void ValidationRejectsEmptyOpenUnknownCandidateAndRegimeConflict()
    {
        HashSet<ActorId> actors = Npcs().ToHashSet();
        Assert.Throws<InvalidDataException>(() => TinyFarmScheduleCatalog.Validate(
            definitions.Schedules.Windows,
            [],
            actors,
            definitions.Scenes));

        TinyFarmUtilityCandidate[] unknown = definitions.Schedules.Candidates
            .Select((candidate, index) => index == 0
                ? candidate with { Anchor = new SceneAnchorId("missing.anchor") }
                : candidate)
            .ToArray();
        Assert.Throws<InvalidDataException>(() => TinyFarmScheduleCatalog.Validate(
            definitions.Schedules.Windows,
            unknown,
            actors,
            definitions.Scenes));

        TinyFarmScheduleWindow open = definitions.Schedules.Windows.Single(
            window => window.Regime == TinyFarmScheduleRegime.Open);
        TinyFarmScheduleWindow conflict = open with
        {
            Id = "mara.conflicting-required",
            Regime = TinyFarmScheduleRegime.Required,
            RequiredAnchor = TinyFarmAnchorIds.FarmHome
        };
        Assert.Throws<InvalidDataException>(() => TinyFarmScheduleCatalog.Validate(
            definitions.Schedules.Windows.Append(conflict).ToArray(),
            definitions.Schedules.Candidates,
            actors,
            definitions.Scenes));
    }

    private static ActorId[] Npcs()
    {
        return [TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela];
    }

    private static SceneAnchorId LegacyAnchor(ActorId actor, int absoluteMinute)
    {
        int day = absoluteMinute / 1440 + 1;
        int minute = absoluteMinute % 1440;
        if (actor == TinyFarmIds.Mara)
        {
            if (day == 6 && minute >= 540 && minute < 1020)
            {
                return TinyFarmAnchorIds.StoreCounter;
            }
            if (day == 7 && minute >= 600 && minute < 1020)
            {
                return TinyFarmAnchorIds.RiversideMeetingPoint;
            }
            return minute < 720
                ? TinyFarmAnchorIds.TownSquare
                : minute < 1020
                    ? TinyFarmAnchorIds.RiversideMeetingPoint
                    : TinyFarmAnchorIds.FarmHome;
        }
        if (actor == TinyFarmIds.Elias)
        {
            return minute >= 720 && minute < 1080
                ? TinyFarmAnchorIds.RiversideMeetingPoint
                : TinyFarmAnchorIds.FarmWorkArea;
        }
        return minute >= 480 && minute < 1080
            ? TinyFarmAnchorIds.StoreCounter
            : TinyFarmAnchorIds.FarmHome;
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
}
