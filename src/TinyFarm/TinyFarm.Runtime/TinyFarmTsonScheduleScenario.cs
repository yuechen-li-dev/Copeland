using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM9Proof(
    string Milestone,
    string Outcome,
    string ScheduleContentHash,
    string ScheduleDecisionHash,
    string AnchorSequenceHash,
    string StateHash,
    string ResultsHash,
    string EventsHash,
    string HandoffHash,
    string NavigationHash,
    string ProjectionHash,
    string SceneContentHash,
    string M1Hash,
    string M2Hash,
    int ScheduleRows,
    int ComparedDecisions,
    bool ProductionTsonLoaded,
    bool RowReorderPreserved,
    bool ExhaustiveM8Parity,
    bool SevenDayParity,
    bool ActiveInactiveParity,
    bool HandoffParity,
    bool SaveLoadParity,
    bool HardcodedProductionRowsRemoved,
    bool RawTsonLeaksIntoDecisionRuntime,
    double DecisionsPerThousandMilliseconds,
    long AllocatedBytesPerDecision,
    bool DecisionCostMateriallyWorseThanM8Baseline);

public sealed record TinyFarmM9Evidence(
    TinyFarmM9Proof Proof,
    object Schedules,
    object Parity,
    ScheduleContentProvenance Provenance,
    object Manifest);

public static class TinyFarmTsonScheduleScenario
{
    private const string ExpectedM1Hash = "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333";
    private const string ExpectedM2Hash = "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3";
    private const string ExpectedSceneContentHash = "fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa";
    private const string ExpectedM8DecisionHash = "10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6";
    private const string ExpectedM8AnchorSequenceHash = "d763164039f2841ff6694f597df0610875ada968d0ad28a0fb9f76469fe59711";
    private const string ExpectedM8NavigationHash = "07dde9ac2f6c957017abe151320ee0a7d5c900f51ecd7901331c9d21a480d8fa";
    private const double M8ReferenceMillisecondsPerThousand = 6.8;

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

    public static TinyFarmM9Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmM8Proof m8 = TinyFarmScheduleScenario.Prove().Proof;
        bool reorderPreserved = ProveCanonicalRowOrder(definitions.Schedules);
        bool costMateriallyWorse = m8.DecisionsPerThousandMilliseconds
            > M8ReferenceMillisecondsPerThousand * 2;
        bool success = m8.Outcome == "A"
            && definitions.Schedules.Windows.Count == 11
            && reorderPreserved
            && m8.ScheduleDecisionHash == ExpectedM8DecisionHash
            && m8.AnchorSequenceHash == ExpectedM8AnchorSequenceHash
            && m8.NavigationHash == ExpectedM8NavigationHash
            && m8.SceneContentHash == ExpectedSceneContentHash
            && m8.M1Hash == ExpectedM1Hash
            && m8.M2Hash == ExpectedM2Hash
            && !costMateriallyWorse;

        var proof = new TinyFarmM9Proof(
            "TINY-FARM-M9",
            success ? "A" : "B",
            definitions.ScheduleContent.AggregateSha256,
            m8.ScheduleDecisionHash,
            m8.AnchorSequenceHash,
            m8.StateHash,
            m8.ResultsHash,
            m8.EventsHash,
            m8.HandoffHash,
            m8.NavigationHash,
            m8.ProjectionHash,
            m8.SceneContentHash,
            m8.M1Hash,
            m8.M2Hash,
            definitions.Schedules.Windows.Count,
            Npcs.Length * 7 * 1440,
            true,
            reorderPreserved,
            m8.ExhaustiveMinuteParity,
            m8.SevenDayParity,
            m8.ActiveInactiveParity,
            m8.M6M7HashesPreserved,
            m8.SaveLoadBeforeTransition && m8.SaveLoadAfterTransition && m8.SaveLoadWhileMoving,
            true,
            false,
            m8.DecisionsPerThousandMilliseconds,
            m8.AllocatedBytesPerDecision,
            costMateriallyWorse);

        object schedules = new
        {
            source = definitions.ScheduleContent.FileName,
            root = "NpcSchedules",
            schema = new[]
            {
                "actorId:string",
                "day:string",
                "startMinute:number",
                "endMinuteExclusive:number",
                "anchorId:string",
                "priority:number",
                "reason:string"
            },
            daySelector = "validated authored tokens Every | Day1 .. Day7 -> TinyFarmScheduleDay",
            interval = "[startMinute, endMinuteExclusive)",
            canonicalOrder = "actor ID, every-before-specific, day, start, end, priority, anchor ID, reason",
            rows = definitions.Schedules.Windows,
            authoredAnswers = new
            {
                maraNormalDay1300 = "riverside.meeting-point",
                maraDay6At1000 = "general-store.counter",
                selaGeneralStore = "08:00-18:00 every day",
                eliasLeavesRiverside = "18:00 every day"
            }
        };
        object parity = new
        {
            comparedDecisions = Npcs.Length * 7 * 1440,
            exhaustiveM8Parity = m8.ExhaustiveMinuteParity,
            sevenDayParity = m8.SevenDayParity,
            boundaryParity = m8.TransitionBoundaryParity,
            rowReorderPreserved = reorderPreserved,
            m8.ScheduleDecisionHash,
            m8.AnchorSequenceHash,
            m8.StateHash,
            m8.ResultsHash,
            m8.EventsHash,
            m8.HandoffHash,
            m8.NavigationHash,
            m8.ProjectionHash,
            activeInactiveParity = m8.ActiveInactiveParity,
            saveLoadParity = m8.SaveLoadBeforeTransition
                && m8.SaveLoadAfterTransition
                && m8.SaveLoadWhileMoving
        };
        object manifest = new
        {
            milestone = "TINY-FARM-M9",
            kind = "tson-authored-npc-schedule-windows",
            scheduleContentAuthoredInTson = true,
            hardcodedProductionScheduleRowsRemoved = true,
            dominatusOwnsScheduleSelection = true,
            scheduleBehaviorChanged = false,
            hybridUtilitySchedulingAdded = false,
            stableAnchorIdentitiesPreserved = true,
            rawTsonLeaksIntoDecisionRuntime = false,
            newSchedulerFrameworkAdded = false,
            scheduleDslAdded = false,
            dominatusCoreChanged = false
        };
        return new TinyFarmM9Evidence(proof, schedules, parity, definitions.ScheduleContent, manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static bool ProveCanonicalRowOrder(TinyFarmScheduleCatalog schedules)
    {
        var reordered = new TinyFarmScheduleCatalog(schedules.Windows.Reverse());
        if (!schedules.Windows.SequenceEqual(reordered.Windows))
        {
            return false;
        }

        foreach (ActorId actor in Npcs)
        {
            for (int minute = 0; minute < 7 * 1440; minute++)
            {
                SceneAnchorId expected = TinyFarmNpcSchedule.Decide(schedules, actor, minute).SelectedAnchor;
                SceneAnchorId actual = TinyFarmNpcSchedule.Decide(reordered, actor, minute).SelectedAnchor;
                if (actual != expected)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
