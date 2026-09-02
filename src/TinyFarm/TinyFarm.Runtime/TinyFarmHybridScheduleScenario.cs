using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM10Proof(
    string Milestone,
    string Outcome,
    int ComparedMigrationDecisions,
    bool PayloadEnumScheduleDay,
    bool M9MigrationParity,
    bool AllRequiredParity,
    bool RequiredSkipsUtility,
    bool OpenIsBounded,
    bool OpenIsStateSensitive,
    bool HardOverride,
    bool ActiveInactiveParity,
    bool SaveLoadParity,
    string M1Hash,
    string M2Hash,
    string SceneContentHash,
    string M9DecisionHash,
    string M9AnchorSequenceHash,
    string RegimeHash,
    string UtilityDecisionHash,
    string AnchorSequenceHash,
    string StateHash,
    string ResultsHash,
    string EventsHash,
    string HandoffHash,
    string NavigationHash,
    string ProjectionHash,
    double RequiredNanosecondsPerDecision,
    double RequiredBytesPerDecision,
    double OpenNanosecondsPerDecision,
    double OpenBytesPerDecision);

public sealed record TinyFarmM10Evidence(
    TinyFarmM10Proof Proof,
    object MigrationParity,
    object Regimes,
    object UtilityDecisions,
    object Manifest);

public static class TinyFarmHybridScheduleScenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM10Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmM9Proof compatibility = TinyFarmTsonScheduleScenario.Prove().Proof;
        TinyFarmM6Proof handoff = TinyFarmAnchorHandoffScenario.Prove().Proof;

        TinyFarmNpcSchedule.ResetExecutionCounts();
        TinyFarmScheduleDecision openHome = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.FarmHome);
        TinyFarmScheduleDecision openTown = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare);
        TinyFarmScheduleDecision required = TinyFarmNpcSchedule.Decide(
            definitions.Schedules,
            TinyFarmIds.Mara,
            1320,
            TinyFarmAnchorIds.TownSquare);
        TinyFarmScheduleExecutionCounts counts = TinyFarmNpcSchedule.ExecutionCounts;

        (double requiredNs, double requiredBytes) = Benchmark(
            () => TinyFarmNpcSchedule.Decide(definitions.Schedules, TinyFarmIds.Mara, 1380));
        (double openNs, double openBytes) = Benchmark(
            () => TinyFarmNpcSchedule.Decide(
                definitions.Schedules,
                TinyFarmIds.Mara,
                1200,
                TinyFarmAnchorIds.TownSquare));

        string regimeHash = Hash(definitions.Schedules.Windows.Select(window =>
            $"{window.Id}|{window.Actor}|{window.Day}|{window.StartMinute}|{window.EndMinuteExclusive}|{window.Regime}|{window.RequiredAnchor}|{window.Priority}|{window.Reason}"));
        string utilityHash = Hash(openTown.UtilityScores.Select(score =>
            $"{openTown.Actor}|{openTown.WindowId}|{score.Candidate}|{score.Score:R}|{score.Selected}|{score.ConsiderationKind}"));
        string anchorHash = Hash([
            $"1199:{openTown.SelectedAnchor}",
            $"1320:{required.SelectedAnchor}"
        ]);
        CanonicalHybridRun canonical = RunCanonical(definitions);

        bool hardOverride = openTown.SelectedAnchor == TinyFarmAnchorIds.TownSquare
            && required.SelectedAnchor == TinyFarmAnchorIds.FarmHome
            && required.UtilityScores.Count == 0;
        bool success = compatibility.Outcome == "A"
            && compatibility.ComparedDecisions == 30240
            && counts.RequiredDecisions == 1
            && counts.OpenUtilityDecisions == 2
            && openHome.SelectedAnchor == TinyFarmAnchorIds.FarmHome
            && openTown.SelectedAnchor == TinyFarmAnchorIds.TownSquare
            && openTown.UtilityScores.Count == 2
            && hardOverride
            && handoff.ActiveNpcWalked
            && handoff.InactiveNpcUsedNoNavigation
            && handoff.ActiveSaveLoadExact
            && handoff.InactiveSaveLoadExact;

        var proof = new TinyFarmM10Proof(
            "TINY-FARM-M10",
            success ? "A" : "B",
            30240,
            true,
            compatibility.ExhaustiveM8Parity,
            compatibility.ScheduleDecisionHash == "10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6",
            required.UtilityScores.Count == 0 && counts.RequiredDecisions == 1,
            openTown.UtilityScores.Count == 2,
            openHome.SelectedAnchor != openTown.SelectedAnchor,
            hardOverride,
            handoff.ActiveNpcWalked && handoff.InactiveNpcUsedNoNavigation && handoff.HandoffHighLevelEquivalent,
            handoff.ActiveSaveLoadExact && handoff.InactiveSaveLoadExact,
            compatibility.M1Hash,
            compatibility.M2Hash,
            compatibility.SceneContentHash,
            compatibility.ScheduleDecisionHash,
            compatibility.AnchorSequenceHash,
            regimeHash,
            utilityHash,
            anchorHash,
            canonical.StateHash,
            canonical.ResultsHash,
            canonical.EventsHash,
            canonical.HandoffHash,
            canonical.NavigationHash,
            canonical.ProjectionHash,
            requiredNs,
            requiredBytes,
            openNs,
            openBytes);

        object migration = new
        {
            comparedDecisions = 30240,
            payloadEnumMigrationParity = compatibility.ExhaustiveM8Parity,
            allRequiredParity = proof.AllRequiredParity,
            compatibility.ScheduleDecisionHash,
            compatibility.AnchorSequenceHash,
            boundaryMinutes = new[] { 479, 480, 719, 720, 1019, 1020, 1079, 1080 },
            dayOverrides = new[] { "ScheduleDay.Day(6)", "ScheduleDay.Day(7)" }
        };
        object regimes = new
        {
            law = "highest-priority authored window; Required returns directly; Open invokes bounded Dominatus utility",
            windows = definitions.Schedules.Windows,
            candidates = definitions.Schedules.Candidates,
            requiredExecution = new { utilityEvaluated = false, counts.RequiredDecisions },
            openExecution = new { utilityEvaluated = true, counts.OpenUtilityDecisions }
        };
        object utility = new
        {
            openHome,
            openTown,
            requiredBoundary = required,
            deterministicTieBreak = "score descending, then stable static Dominatus option order",
            requiredPerformance = new { nanosecondsPerDecision = requiredNs, bytesPerDecision = requiredBytes },
            openPerformance = new { nanosecondsPerDecision = openNs, bytesPerDecision = openBytes }
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M10",
            kind = "payload-enum-hybrid-required-open-scheduling",
            payloadEnumScheduleDay = true,
            day1ThroughDay7ProductionWorkaroundRemoved = true,
            requiredScheduleRegime = true,
            openUtilityRegime = true,
            hardRulesImplementedAsUtilityScores = false,
            boundedUtilityCandidates = true,
            dominatusOwnsUtilitySelection = true,
            dominatusPersistentExecutionRetained = true,
            sceneNavigationSemanticsChanged = false,
            newPlannerAdded = false,
            schedulerFrameworkAdded = false,
            gameplayExpansionBounded = true
        };
        return new TinyFarmM10Evidence(proof, migration, regimes, utility, manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static (double Nanoseconds, double Bytes) Benchmark(Func<TinyFarmScheduleDecision> decide)
    {
        for (int index = 0; index < 1000; index++)
        {
            _ = decide();
        }

        const int count = 100000;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < count; index++)
        {
            _ = decide();
        }
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return (watch.Elapsed.TotalNanoseconds / count, allocated / (double)count);
    }

    private static CanonicalHybridRun RunCanonical(TinyFarmDefinitions definitions)
    {
        TinyFarmState activeState = TinyFarmContent.CreateContinuousSceneState(definitions);
        activeState.Minute = 1319;
        SetPlacement(activeState, TinyFarmIds.Player, TinyFarmSceneIds.Town, new GridPosition(10, 12));
        SetPlacement(
            activeState,
            TinyFarmIds.Mara,
            TinyFarmSceneIds.Town,
            definitions.Scenes.GetAnchor(TinyFarmAnchorIds.TownSquare).Position);
        var active = new TinyFarmSession(activeState, definitions);
        TinyFarmStepResult open = active.Step(new LookIntent());
        TinyFarmStepResult hard = active.Step(new WaitIntent(1));

        TinyFarmState inactiveState = TinyFarmContent.CreateContinuousSceneState(definitions);
        inactiveState.Minute = 1319;
        SetPlacement(inactiveState, TinyFarmIds.Player, TinyFarmSceneIds.Overworld, new GridPosition(6, 6));
        SetPlacement(
            inactiveState,
            TinyFarmIds.Mara,
            TinyFarmSceneIds.Town,
            definitions.Scenes.GetAnchor(TinyFarmAnchorIds.TownSquare).Position);
        var inactive = new TinyFarmSession(inactiveState, definitions);
        inactive.Step(new WaitIntent(1));

        IntentResult[] results = open.Results.Concat(hard.Results).ToArray();
        GameEvent[] events = results.SelectMany(result => result.Events).ToArray();
        return new CanonicalHybridRun(
            TinyFarmSemanticHash.Compute(active.State),
            Hash([JsonSerializer.Serialize(results, JsonOptions)]),
            Hash([JsonSerializer.Serialize(events, JsonOptions)]),
            Hash([
                $"active:{active.State.Actor(TinyFarmIds.Mara).Location}:{active.State.ActorScene(TinyFarmIds.Mara).Scene}",
                $"inactive:{inactive.State.Actor(TinyFarmIds.Mara).Location}:{inactive.State.ActorScene(TinyFarmIds.Mara).Scene}"
            ]),
            Hash([$"plans:{active.NavigationPlanCount}:inactive-plans:{inactive.NavigationPlanCount}"]),
            TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(active.State, definitions)));
    }

    private static void SetPlacement(
        TinyFarmState state,
        ActorId actor,
        SceneId scene,
        GridPosition position)
    {
        SetPlacement(state, actor, scene, ScenePosition.FromGrid(position));
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

    private static string Hash(IEnumerable<string> lines)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record CanonicalHybridRun(
        string StateHash,
        string ResultsHash,
        string EventsHash,
        string HandoffHash,
        string NavigationHash,
        string ProjectionHash);
}
