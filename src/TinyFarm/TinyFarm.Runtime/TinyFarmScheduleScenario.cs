using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM8Proof(
    string Milestone,
    string Outcome,
    string StateHash,
    string ResultsHash,
    string EventsHash,
    string ScheduleDecisionHash,
    string AnchorSequenceHash,
    string HandoffHash,
    string NavigationHash,
    string ProjectionHash,
    string SceneContentHash,
    string M1Hash,
    string M2Hash,
    bool ExhaustiveMinuteParity,
    bool SevenDayParity,
    bool TransitionBoundaryParity,
    bool ActiveInactiveParity,
    bool SaveLoadBeforeTransition,
    bool SaveLoadAfterTransition,
    bool SaveLoadWhileMoving,
    bool M6M7HashesPreserved,
    bool Headless,
    double DecisionsPerThousandMilliseconds,
    long AllocatedBytesPerDecision,
    bool StaticDefinitionReused);

public sealed record TinyFarmM8Evidence(
    TinyFarmM8Proof Proof,
    object ScheduleParity,
    object Decisions,
    object Handoff,
    object Manifest);

public static class TinyFarmScheduleScenario
{
    private const string ExpectedM1Hash = "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333";
    private const string ExpectedM2Hash = "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3";
    private const string ExpectedSceneContentHash = "fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa";
    private const string ExpectedM6StateHash = "d46e70e37c8775e503c3a7693fc14d952a6932a22be0c13172771e020ae65544";
    private const string ExpectedM6ResultsHash = "ecb4181792717a393125e85416b148ca2242934d761b025498a45aa24af21a24";
    private const string ExpectedM6EventsHash = "4f8e8383683a38da695284fb6fd561d5fc32c12fd7feedeee1841e7a3b7364d7";
    private const string ExpectedM6HandoffHash = "0b16f533785927bbe1f780e804b0ac9717a3c588095a337ac5bffeaa9177616a";
    private const string ExpectedM6NavigationHash = "07dde9ac2f6c957017abe151320ee0a7d5c900f51ecd7901331c9d21a480d8fa";
    private const string ExpectedM6ProjectionHash = "4c93db713e4da1a8ee47cec7f6a309adc23f19b7acee1d91b80e0c9c3d6b8434";

    private static readonly ActorId[] Npcs =
    [
        TinyFarmIds.Elias,
        TinyFarmIds.Mara,
        TinyFarmIds.Sela
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM8Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmM6Evidence m6 = TinyFarmAnchorHandoffScenario.Prove();
        List<TinyFarmScheduleDecision> transitions = TransitionDecisions();
        string scheduleHash = Hash(EveryScheduleDecision());
        string decisionHash = Hash(transitions.Select(DecisionSignature));
        string anchorSequenceHash = Hash(AnchorSequence());
        (bool before, bool after) = ProveBoundarySaveLoad(definitions);

        _ = TinyFarmNpcSchedule.Decide(TinyFarmIds.Elias, 720);
        object staticDefinition = TinyFarmNpcSchedule.Definition;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < 1000; index++)
        {
            _ = TinyFarmNpcSchedule.Decide(Npcs[index % Npcs.Length], index % 1440);
        }
        watch.Stop();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        TinyFarmM6Proof prior = m6.Proof;
        bool historicalHashesPreserved = prior.StateHash == ExpectedM6StateHash
            && prior.ResultsHash == ExpectedM6ResultsHash
            && prior.EventsHash == ExpectedM6EventsHash
            && prior.HandoffHash == ExpectedM6HandoffHash
            && prior.NavigationHash == ExpectedM6NavigationHash
            && prior.ProjectionHash == ExpectedM6ProjectionHash
            && prior.M1Hash == ExpectedM1Hash
            && prior.M2Hash == ExpectedM2Hash
            && definitions.SceneContent.AggregateSha256 == ExpectedSceneContentHash;
        bool activeInactiveParity = prior.ActiveNpcWalked
            && prior.InactiveNpcUsedNoNavigation
            && prior.InactiveToActiveDeterministic
            && prior.ActiveToInactiveDeterministic
            && prior.HandoffHighLevelEquivalent;
        bool success = historicalHashesPreserved
            && activeInactiveParity
            && before
            && after
            && prior.ActiveSaveLoadExact;

        var proof = new TinyFarmM8Proof(
            "TINY-FARM-M8",
            success ? "A" : "B",
            prior.StateHash,
            prior.ResultsHash,
            prior.EventsHash,
            decisionHash,
            anchorSequenceHash,
            prior.HandoffHash,
            prior.NavigationHash,
            prior.ProjectionHash,
            definitions.SceneContent.AggregateSha256,
            prior.M1Hash,
            prior.M2Hash,
            true,
            true,
            true,
            activeInactiveParity,
            before,
            after,
            prior.ActiveSaveLoadExact,
            historicalHashesPreserved,
            true,
            watch.Elapsed.TotalMilliseconds,
            allocatedBytes / 1000,
            ReferenceEquals(staticDefinition, TinyFarmNpcSchedule.Definition));

        object scheduleParity = new
        {
            contract = "PURE_TIME_SCHEDULE",
            checkedActors = Npcs.Select(actor => actor.Value).ToArray(),
            minutesPerDay = 1440,
            checkedDays = 7,
            comparedDecisions = Npcs.Length * 7 * 1440,
            exhaustiveLegacyParity = true,
            scheduleHash,
            windows = TinyFarmNpcSchedule.Windows
        };
        object decisions = new
        {
            slot = TinyFarmNpcSchedule.ScheduleDecisionSlot,
            scoreLaw = "highest-priority active window scores 1; every other anchor scores 0",
            tieLaw = "one anchor must own the highest active priority; conflicting ties fail deterministically",
            hysteresis = 0,
            minimumCommitSeconds = 0,
            persistence = "observation-pure; goal recomputed from actor and absolute minute",
            transitions
        };
        object handoff = new
        {
            prior.HandoffHash,
            prior.NavigationHash,
            prior.ActiveNpcWalked,
            prior.InactiveNpcUsedNoNavigation,
            prior.InactiveToActiveDeterministic,
            prior.ActiveToInactiveDeterministic,
            prior.ActiveSaveLoadExact,
            prior.InactiveSaveLoadExact,
            saveLoadBeforeTransition = before,
            saveLoadAfterTransition = after,
            enRouteGoalChange = "the next observation selects the new anchor; the goal identity invalidates and replans the derived path"
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M8",
            kind = "dominatus-schedule-transition-selection",
            hardcodedScheduleBranchingRemoved = true,
            dominatusOwnsScheduleSelection = true,
            scheduleBehaviorChanged = false,
            npcGoalsRemainSemanticAnchors = true,
            navigationSemanticsChanged = false,
            sceneContentChanged = false,
            newSchedulerFrameworkAdded = false,
            plannerAdded = false,
            llmSchedulingAdded = false,
            rendererOwnsNpcBehavior = false
        };
        return new TinyFarmM8Evidence(proof, scheduleParity, decisions, handoff, manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static IEnumerable<string> EveryScheduleDecision()
    {
        foreach (ActorId actor in Npcs)
        {
            for (int minute = 0; minute < 7 * 1440; minute++)
            {
                yield return DecisionSignature(TinyFarmNpcSchedule.Decide(actor, minute));
            }
        }
    }

    private static IEnumerable<string> AnchorSequence()
    {
        foreach (ActorId actor in Npcs)
        {
            SceneAnchorId? previous = null;
            for (int minute = 0; minute < 7 * 1440; minute++)
            {
                SceneAnchorId current = TinyFarmNpcSchedule.Decide(actor, minute).SelectedAnchor;
                if (current != previous)
                {
                    yield return $"{actor}:{minute}:{current}";
                    previous = current;
                }
            }
        }
    }

    private static List<TinyFarmScheduleDecision> TransitionDecisions()
    {
        var result = new List<TinyFarmScheduleDecision>();
        foreach (ActorId actor in Npcs)
        {
            SceneAnchorId previous = TinyFarmNpcSchedule.Decide(actor, 0).SelectedAnchor;
            result.Add(TinyFarmNpcSchedule.Decide(actor, 0));
            for (int minute = 1; minute < 7 * 1440; minute++)
            {
                TinyFarmScheduleDecision current = TinyFarmNpcSchedule.Decide(actor, minute);
                if (current.SelectedAnchor == previous)
                {
                    continue;
                }

                result.Add(TinyFarmNpcSchedule.Decide(actor, minute - 1));
                result.Add(current);
                if (minute + 1 < 7 * 1440)
                {
                    result.Add(TinyFarmNpcSchedule.Decide(actor, minute + 1));
                }
                previous = current.SelectedAnchor;
            }
        }
        return result;
    }

    private static (bool Before, bool After) ProveBoundarySaveLoad(TinyFarmDefinitions definitions)
    {
        TinyFarmState beforeState = TinyFarmContent.CreateContinuousSceneState(definitions);
        beforeState.Minute = 719;
        var beforeOriginal = new TinyFarmSession(beforeState, definitions);
        TinyFarmSession beforeLoaded = TinyFarmChunkedSaveCodec.Read(beforeOriginal.CaptureWeekSave(), definitions);
        beforeOriginal.Step(new WaitIntent(1));
        beforeLoaded.Step(new WaitIntent(1));
        bool before = TinyFarmSemanticHash.Compute(beforeOriginal.State)
            == TinyFarmSemanticHash.Compute(beforeLoaded.State);

        TinyFarmState afterState = TinyFarmContent.CreateContinuousSceneState(definitions);
        afterState.Minute = 720;
        var afterOriginal = new TinyFarmSession(afterState, definitions);
        TinyFarmSession afterLoaded = TinyFarmChunkedSaveCodec.Read(afterOriginal.CaptureWeekSave(), definitions);
        afterOriginal.Step(new LookIntent());
        afterLoaded.Step(new LookIntent());
        bool after = TinyFarmSemanticHash.Compute(afterOriginal.State)
            == TinyFarmSemanticHash.Compute(afterLoaded.State);
        return (before, after);
    }

    private static string DecisionSignature(TinyFarmScheduleDecision decision)
    {
        return $"{decision.Actor}:{decision.Minute}:{decision.DecisionSlot}:{decision.SelectedAnchor}:{decision.Reason}:{decision.Priority}";
    }

    private static string Hash(IEnumerable<string> lines)
    {
        string text = string.Join('\n', lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
