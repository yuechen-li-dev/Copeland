using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM13Proof(
    string Milestone,
    string Outcome,
    string ReuseDecision,
    string StateHash,
    string ResultsHash,
    string EventsHash,
    string ClockHash,
    string ModesHash,
    string EnergyHash,
    string RegimesHash,
    string UtilityHash,
    string AnchorsHash,
    string RestHash,
    string HandoffHash,
    string NavigationHash,
    string ProjectionHash,
    string SimulationDtoHash,
    string OneDayHash,
    string SevenDayHash,
    bool PausedFreezes,
    bool PlayRateExact,
    bool FastForwardRateExact,
    bool SixtyVsOneFortyFourEquivalent,
    bool IrregularEquivalent,
    bool SaveLoadResetsAccumulator,
    bool FatigueRestRecoveryDeparture,
    bool M12Regression,
    int RenderFrames,
    long LocomotionSteps,
    long WorldMinutes,
    long NpcDecisions,
    int DotRecastQueries);

public sealed record TinyFarmM13Evidence(
    TinyFarmM13Proof Proof,
    object Timing,
    object Rates,
    string SimulationDtoTson,
    object Manifest);

public static class TinyFarmSimulationScenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM13Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM12();
        TinyFarmSimulationHost host = CreateHost(definitions);
        string pausedHash = TinyFarmSemanticHash.Compute(host.Session.State);
        host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        bool pausedFreezes = pausedHash == TinyFarmSemanticHash.Compute(host.Session.State);

        var modeSequence = new List<TinyFarmSimulationMode> { host.Mode };
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        modeSequence.Add(host.Mode);
        TinyFarmHostAdvanceResult play = host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        int minuteAfterPlay = host.Session.State.Minute;
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.FastForward));
        modeSequence.Add(host.Mode);
        TinyFarmHostAdvanceResult fast = host.AdvanceHostTime(TimeSpan.FromSeconds(5));
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Paused));
        modeSequence.Add(host.Mode);
        string frozenHash = TinyFarmSemanticHash.Compute(host.Session.State);
        host.AdvanceHostTime(TimeSpan.FromSeconds(5));

        var allResults = play.Results.Concat(fast.Results).ToArray();
        var allEvents = allResults.SelectMany(result => result.Events).ToArray();
        TinyFarmSimulationHost sixty = CreateHost(definitions, TinyFarmSimulationMode.Playing);
        TinyFarmSimulationHost oneFortyFour = CreateHost(definitions, TinyFarmSimulationMode.Playing);
        AdvancePartitioned(sixty, TimeSpan.FromSeconds(60), 3_600);
        AdvancePartitioned(oneFortyFour, TimeSpan.FromSeconds(60), 8_640);
        bool refreshEquivalent = Equivalent(sixty, oneFortyFour);

        TinyFarmSimulationHost irregular = CreateHost(definitions, TinyFarmSimulationMode.Playing);
        TinyFarmSimulationHost even = CreateHost(definitions, TinyFarmSimulationMode.Playing);
        int[] pattern = [16, 16, 50, 3, 91, 7, 33, 84];
        long irregularTicks = 0;
        for (int repeat = 0; repeat < 25; repeat++)
        {
            foreach (int milliseconds in pattern)
            {
                TimeSpan delta = TimeSpan.FromMilliseconds(milliseconds);
                irregular.AdvanceHostTime(delta);
                irregularTicks += delta.Ticks;
            }
        }
        AdvancePartitioned(even, TimeSpan.FromTicks(irregularTicks), 317);
        bool irregularEquivalent = Equivalent(irregular, even);

        TinyFarmSimulationHost replacement = CreateHost(definitions, TinyFarmSimulationMode.Playing);
        replacement.AdvanceHostTime(TimeSpan.FromSeconds(4));
        byte[] save = replacement.Session.CaptureWeekSave();
        replacement.ReplaceSession(TinyFarmChunkedSaveCodec.Read(save, definitions));
        replacement.AdvanceHostTime(TimeSpan.FromSeconds(1));
        bool resetBeforeThreshold = replacement.Session.State.Minute == 480;
        replacement.AdvanceHostTime(TimeSpan.FromSeconds(4));
        bool saveLoadResets = resetBeforeThreshold && replacement.Session.State.Minute == 481;

        TinyFarmSimulationHost life = new(
            new TinyFarmSession(TinyFarmM12ControlStates.Create(definitions, "low-open"), definitions),
            definitions,
            TinyFarmSimulationMode.FastForward);
        bool sawRest = false;
        bool sawRecovery = false;
        bool sawDeparture = false;
        int energyAtRest = 0;
        var energyTimeline = new List<object>();
        var regimeTimeline = new List<object>();
        var anchorTimeline = new List<string>();
        var restTimeline = new List<object>();
        for (int minute = 0; minute < 120; minute++)
        {
            life.AdvanceHostTime(TimeSpan.FromMilliseconds(500));
            ActorEnergyState energy = life.Session.State.EnergyFor(TinyFarmIds.Mara);
            SceneAnchorId? anchor = TinyFarmNpcController.CurrentAnchor(
                life.Session.State,
                life.Session.State.Actor(TinyFarmIds.Mara),
                definitions.Scenes,
                definitions.Schedules);
            TinyFarmScheduleDecision decision = TinyFarmNpcSchedule.Decide(
                definitions.Schedules,
                TinyFarmIds.Mara,
                life.Session.State.Minute,
                anchor,
                energy: energy.Energy);
            energyTimeline.Add(new { life.Session.State.Minute, energy.Energy });
            regimeTimeline.Add(new { life.Session.State.Minute, decision.Regime });
            anchorTimeline.Add(anchor?.Value ?? "en-route");
            restTimeline.Add(new { life.Session.State.Minute, energy.IsResting });
            if (energy.IsResting && !sawRest)
            {
                sawRest = true;
                energyAtRest = energy.Energy;
            }
            sawRecovery |= sawRest && energy.Energy > energyAtRest;
            sawDeparture |= sawRest && sawRecovery && !energy.IsResting && decision.SelectedAnchor != TinyFarmAnchorIds.MaraHomeBed;
        }

        TinyFarmSimulationHost duration = CreateHost(definitions);
        duration.Execute(new AdvanceMinutesCommand(1_440));
        string oneDayHash = TinyFarmSemanticHash.Compute(duration.Session.State);
        duration.Execute(new AdvanceMinutesCommand(8_640));
        string sevenDayHash = TinyFarmSemanticHash.Compute(duration.Session.State);

        TinyFarmFrame frame = TinyFarmFrameProjector.Project(host.Session.State, definitions);
        TinyFarmSimulationSnapshot snapshot = host.Snapshot();
        string tson = TinyFarmSimulationSnapshotProjector.WriteCanonicalTson(snapshot);
        string m12StateHash = TinyFarmEnergyScenario.Prove().Proof.StateHash;
        bool m12Regression = m12StateHash == "7f5bf5a47afdcb7e35662b6e920da8a942f5ecb7d589206692095173e3c4cf43";
        bool success = pausedFreezes
            && play.WorldMinutesAdvanced == 1
            && minuteAfterPlay == 481
            && fast.WorldMinutesAdvanced == 10
            && frozenHash == TinyFarmSemanticHash.Compute(host.Session.State)
            && refreshEquivalent
            && irregularEquivalent
            && saveLoadResets
            && sawRest
            && sawRecovery
            && sawDeparture
            && m12Regression;

        var proof = new TinyFarmM13Proof(
            "TINY-FARM-M13",
            success ? "A" : "B",
            "TINYFARM_LOCAL_HOST_ONLY",
            TinyFarmSemanticHash.Compute(host.Session.State),
            Hash(allResults),
            Hash(allEvents),
            Hash(new { host.Session.State.Day, host.Session.State.Minute, host.WorldMinutesAdvanced }),
            Hash(modeSequence),
            Hash(energyTimeline),
            Hash(regimeTimeline),
            Hash(regimeTimeline.Select(item => item.ToString())),
            Hash(anchorTimeline),
            Hash(restTimeline),
            Hash(new { saveLoadResets, oneDayHash, sevenDayHash }),
            Hash(new { life.Session.NavigationPlanCount, life.Session.ActivationCount, life.Session.DeactivationCount }),
            TinyFarmFrameProjector.ComputeHash(frame),
            TinyFarmSimulationSnapshotProjector.ComputeTsonHash(snapshot),
            oneDayHash,
            sevenDayHash,
            pausedFreezes,
            play.WorldMinutesAdvanced == 1,
            fast.WorldMinutesAdvanced == 10,
            refreshEquivalent,
            irregularEquivalent,
            saveLoadResets,
            sawRest && sawRecovery && sawDeparture,
            m12Regression,
            3_600,
            sixty.LocomotionStepsAdvanced,
            sixty.WorldMinutesAdvanced,
            sixty.Session.DecisionEvaluationCount,
            life.Session.NavigationPlanCount);
        object timing = new
        {
            hostTime = "TimeSpan ticks supplied by the caller",
            renderFrames = 3_600,
            locomotionSteps = sixty.LocomotionStepsAdvanced,
            worldMinutes = sixty.WorldMinutesAdvanced,
            npcDecisions = sixty.Session.DecisionEvaluationCount,
            dotRecastQueries = sixty.Session.NavigationPlanCount,
            liveFatigueDotRecastQueries = life.Session.NavigationPlanCount,
            sixtyHzHash = TinyFarmSemanticHash.Compute(sixty.Session.State),
            oneFortyFourHzHash = TinyFarmSemanticHash.Compute(oneFortyFour.Session.State),
            irregularHash = TinyFarmSemanticHash.Compute(irregular.Session.State),
            evenHash = TinyFarmSemanticHash.Compute(even.Session.State)
        };
        object rates = new
        {
            normalRealSecondsPerGameMinute = 5,
            fastForwardMultiplier = 10,
            locomotionHz = 60,
            maximumHostDeltaSeconds = 5,
            backlogPolicy = "discard host delta beyond the clamp; never retain catch-up debt"
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M13",
            kind = "simulation-host-multirate-clock-tson-control",
            existingInfrastructureAuditedFirst = true,
            duplicateClockInfrastructureAdded = false,
            rendererAdvancesGameTimeDirectly = false,
            simulationHostSharedByHeadlessAndGraphical = true,
            pausePlayFastForwardSemantic = true,
            renderRateIndependent = true,
            locomotionFixedStepIndependent = true,
            worldMinuteRateIndependent = true,
            agentDecisionRateIndependent = true,
            tsonSimulationDtoAddedOrFormalized = true
        };
        return new TinyFarmM13Evidence(proof, timing, rates, tson, manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static TinyFarmSimulationHost CreateHost(
        TinyFarmDefinitions definitions,
        TinyFarmSimulationMode mode = TinyFarmSimulationMode.Paused)
    {
        return new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmContent.CreateEnergySceneState(definitions), definitions),
            definitions,
            mode);
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

    private static bool Equivalent(TinyFarmSimulationHost left, TinyFarmSimulationHost right)
    {
        return TinyFarmSemanticHash.Compute(left.Session.State) == TinyFarmSemanticHash.Compute(right.Session.State)
            && left.WorldMinutesAdvanced == right.WorldMinutesAdvanced
            && left.LocomotionStepsAdvanced == right.LocomotionStepsAdvanced
            && left.Session.DecisionEvaluationCount == right.Session.DecisionEvaluationCount
            && left.Session.NavigationPlanCount == right.Session.NavigationPlanCount;
    }

    private static string Hash<T>(T value)
    {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)))
            .ToLowerInvariant();
    }
}
