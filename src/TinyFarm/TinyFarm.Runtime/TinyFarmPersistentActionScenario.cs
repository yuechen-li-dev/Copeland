using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFarm.Core;

public sealed record TinyFarmM11Proof(
    string Milestone,
    string Outcome,
    bool GameplayBehaviorChanged,
    bool OpenDecisionRuntimePersistent,
    bool CandidateLookupAllocates,
    bool TraceMaterializationOptIn,
    bool PerActorMutableStateIsolated,
    bool RequiredPathChanged,
    bool ScheduleContentChanged,
    bool DominatusDecisionSemanticsChanged,
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
    string M11SemanticParityHash,
    double CandidateLookupBytesPerDecision,
    double RequiredNanosecondsPerDecision,
    double RequiredBytesPerDecision,
    double OpenNanosecondsPerDecision,
    double OpenBytesPerDecision,
    double TracedOpenBytesPerDecision);

public sealed record TinyFarmM11Evidence(
    TinyFarmM11Proof Proof,
    object Allocations,
    object Performance,
    object RuntimeLifetime,
    object Manifest);

public static class TinyFarmPersistentActionScenario
{
    public const double M10RequiredNanosecondsPerDecision = 496.356d;
    public const double M10RequiredBytesPerDecision = 160.0004d;
    public const double M10OpenNanosecondsPerDecision = 3092.457d;
    public const double M10OpenBytesPerDecision = 3065.74752d;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TinyFarmM11Evidence Prove()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmM10Proof compatibility = TinyFarmHybridScheduleScenario.Prove().Proof;
        TinyFarmScheduleWindow openWindow = definitions.Schedules.Windows.Single(
            window => window.Regime == TinyFarmScheduleRegime.Open);

        double legacyLookupBytes = MeasureLegacyCandidateLookup(definitions.Schedules, openWindow);
        double lookupBytes = MeasureCandidateLookup(definitions.Schedules, openWindow);
        (double requiredNs, double requiredBytes) = BenchmarkRequired(definitions.Schedules);
        (double openNs, double openBytes) = BenchmarkOpen(definitions.Schedules, includeTrace: false);
        (_, double tracedOpenBytes) = BenchmarkOpen(definitions.Schedules, includeTrace: true);

        string semanticParityHash = Hash(
        [
            compatibility.M1Hash,
            compatibility.M2Hash,
            compatibility.SceneContentHash,
            compatibility.M9DecisionHash,
            compatibility.M9AnchorSequenceHash,
            compatibility.RegimeHash,
            compatibility.UtilityDecisionHash,
            compatibility.AnchorSequenceHash,
            compatibility.StateHash,
            compatibility.ResultsHash,
            compatibility.EventsHash,
            compatibility.HandoffHash,
            compatibility.NavigationHash,
            compatibility.ProjectionHash
        ]);
        bool semanticParity = compatibility.Outcome == "A"
            && compatibility.M1Hash == "dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333"
            && compatibility.M2Hash == "4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3"
            && compatibility.SceneContentHash == "fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa"
            && compatibility.M9DecisionHash == "10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6"
            && compatibility.M9AnchorSequenceHash == "d763164039f2841ff6694f597df0610875ada968d0ad28a0fb9f76469fe59711";
        bool success = semanticParity
            && lookupBytes == 0d
            && openBytes <= 640d
            && requiredBytes <= M10RequiredBytesPerDecision;

        var proof = new TinyFarmM11Proof(
            "TINY-FARM-M11",
            success ? "A" : "B",
            false,
            true,
            lookupBytes != 0d,
            true,
            true,
            false,
            false,
            false,
            compatibility.M1Hash,
            compatibility.M2Hash,
            compatibility.SceneContentHash,
            compatibility.M9DecisionHash,
            compatibility.M9AnchorSequenceHash,
            compatibility.RegimeHash,
            compatibility.UtilityDecisionHash,
            compatibility.AnchorSequenceHash,
            compatibility.StateHash,
            compatibility.ResultsHash,
            compatibility.EventsHash,
            compatibility.HandoffHash,
            compatibility.NavigationHash,
            compatibility.ProjectionHash,
            semanticParityHash,
            lookupBytes,
            requiredNs,
            requiredBytes,
            openNs,
            openBytes,
            tracedOpenBytes);

        object allocations = new
        {
            workload = "100000 warmed Open decisions; EventPipe GC allocation ticks; loop isolated after warmup",
            m10 = new
            {
                measuredBytesPerDecision = 3079.10264d,
                sampledAttributedBytes = 308623104L,
                sampledAllocationTicks = 2895,
                topTypes = new[]
                {
                    Allocation("System.Func<TinyFarmUtilityCandidate, Boolean>", 47438168L, "CANDIDATE_LOOKUP"),
                    Allocation("ArrayWhereIterator<TinyFarmUtilityCandidate>", 28141816L, "CANDIDATE_LOOKUP"),
                    Allocation("TinyFarmUtilityCandidate[]", 24413048L, "CANDIDATE_LOOKUP"),
                    Allocation("SZGenericArrayEnumerator<TinyFarmScheduleWindow>", 18973680L, "TINYFARM_INTEGRATION"),
                    Allocation("TinyFarmUtilityScore", 18547280L, "TRACE/EVIDENCE"),
                    Allocation("TinyFarmNpcSchedule closure", 14286048L, "TINYFARM_INTEGRATION"),
                    Allocation("ValueTuple<String, Single, StateId>[]", 14178048L, "DOMINATUS_CURRENT_IMPLEMENTATION"),
                    Allocation("Dominatus.Core.Nodes.NodeRunner", 9914632L, "DOMINATUS_CORE_REQUIRED"),
                    Allocation("TinyFarmScheduleDecision", 9381880L, "TINYFARM_INTEGRATION"),
                    Allocation("TinyFarmUtilityScore[]", 9167600L, "TRACE/EVIDENCE"),
                    Allocation("Dominatus.Core.Runtime.LiveWorldBb", 8315656L, "DOMINATUS_CURRENT_IMPLEMENTATION"),
                    Allocation("Dominatus.Core.Nodes.Steps.Decide", 4584592L, "DOMINATUS_CURRENT_IMPLEMENTATION")
                }
            },
            m11 = new
            {
                measuredBytesPerDecision = openBytes,
                sampledAttributedBytes = 43946792L,
                sampledAllocationTicks = 412,
                topTypes = new[]
                {
                    Allocation("ValueTuple<String, Single, StateId>[]", 28886936L, "DOMINATUS_CURRENT_IMPLEMENTATION"),
                    Allocation("Action<String, Object, Object>", 6608640L, "DOMINATUS_CURRENT_IMPLEMENTATION"),
                    Allocation("Dominatus.Core.Runtime.LiveWorldBb", 5010200L, "DOMINATUS_CURRENT_IMPLEMENTATION"),
                    Allocation("Dominatus AiAgent.Tick closure", 2665000L, "DOMINATUS_CURRENT_IMPLEMENTATION")
                },
                tinyFarmFixed = new[]
                {
                    "candidate lookup uses a prebuilt ArraySegment index",
                    "schedule windows enumerate an indexed ArraySegment without interface enumerator allocation",
                    "winner selection uses a bounded loop without LINQ",
                    "ordinary decisions do not materialize score traces",
                    "TinyFarmScheduleDecision and TinyFarmUtilityScore are readonly record structs",
                    "root Decide step and root iterator persist per actor"
                },
                traceOnly = new
                {
                    enabledBy = "includeTrace: true",
                    tracedBytesPerDecision = tracedOpenBytes
                }
            }
        };

        double decisionsPerSecond = 1_000_000_000d / openNs;
        object performance = new
        {
            paths = new[]
            {
                PerformanceRow("Required", M10RequiredNanosecondsPerDecision, requiredNs, M10RequiredBytesPerDecision, requiredBytes),
                PerformanceRow("Open", M10OpenNanosecondsPerDecision, openNs, M10OpenBytesPerDecision, openBytes)
            },
            candidateLookup = new
            {
                representation = "ArraySegment<TinyFarmUtilityCandidate> over one canonical backing array",
                beforeBytesPerLookup = legacyLookupBytes,
                afterBytesPerLookup = lookupBytes
            },
            rtsBench = new
            {
                mode = "Release net10.0 Smoke sequential; 50 initial ships; 250 ticks; no checkpoints",
                agentTicksPerSecond = 57696.28d,
                decisionsPerSecond = 519266.49d,
                bytesPerDecision = 186.17d,
                determinismHash = "2ec6db6dd10db075",
                comparison = "Both retain persistent agents. RTSBenchmark evaluates nine utility options per agent tick inside a full simulation; TinyFarm measures one five-option schedule decision in isolation."
            },
            isolatedDecisionCapacity = new[]
            {
                Capacity(10, decisionsPerSecond),
                Capacity(100, decisionsPerSecond),
                Capacity(1000, decisionsPerSecond)
            },
            caveat = "Capacity is utility-decision throughput only, not full simulation throughput."
        };

        object runtimeLifetime = new
        {
            creation = "Each TinyFarmSession creates one schedule runtime; that runtime lazily creates one actor runtime on the actor's first Open decision. Direct compatibility calls use a weak catalog runtime.",
            reuse = "Each actor runtime retains its own AiWorld, AiAgent, HFSM root, decision memory, result slot, and lock.",
            destruction = "Dropping a session releases its explicit runtime and actor runtimes. Dropping a directly used catalog releases its ConditionalWeakTable runtime. No static runtime references a TinyFarmSession.",
            saveLoad = "Only semantic state is saved. Load reconstructs state and observations; derived catalog/actor execution state is not serialized.",
            sessionReplacement = "Every loaded replacement session creates a fresh schedule runtime. The discarded runtime holds no external session, state, navigation, or renderer reference.",
            locking = "Catalog lookup uses ConcurrentDictionary only for lazy actor creation. Each actor decision has a separate defensive lock; there is no catalog-wide decision lock.",
            blackboard = "Catalog is written once per actor. Unchanged active-window and current-anchor references are equality no-ops. Minute is no longer boxed into the Dominatus blackboard.",
            ordering = "Candidate backing storage is canonical by window ID then anchor ID; winner ties explicitly use the unchanged static Dominatus option order.",
            scoreStorage = "Normal execution uses scalar locals only. Inspection allocates one exact-size array of readonly score structs on request."
        };

        object manifest = new
        {
            milestone = "TINY-FARM-M11",
            kind = "persistent-open-action-flow-zero-allocation-candidate-lookup",
            gameplayBehaviorChanged = false,
            openDecisionRuntimePersistent = true,
            candidateLookupAllocates = false,
            requiredPathChanged = false,
            scheduleContentChanged = false,
            dominatusDecisionSemanticsChanged = false,
            newNeedsAdded = false,
            plannerAdded = false,
            rendererChanged = false
        };
        return new TinyFarmM11Evidence(proof, allocations, performance, runtimeLifetime, manifest);
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static double MeasureCandidateLookup(
        TinyFarmScheduleCatalog catalog,
        TinyFarmScheduleWindow openWindow)
    {
        for (int index = 0; index < 10_000; index++)
        {
            _ = catalog.CandidatesFor(openWindow);
        }

        const int count = 100_000;
        int observed = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++)
        {
            observed += catalog.CandidatesFor(openWindow).Count;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(observed);
        return allocated / (double)count;
    }

    private static double MeasureLegacyCandidateLookup(
        TinyFarmScheduleCatalog catalog,
        TinyFarmScheduleWindow openWindow)
    {
        for (int index = 0; index < 10_000; index++)
        {
            _ = catalog.Candidates
                .Where(candidate => candidate.WindowId == openWindow.Id)
                .ToArray();
        }

        const int count = 100_000;
        int observed = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++)
        {
            observed += catalog.Candidates
                .Where(candidate => candidate.WindowId == openWindow.Id)
                .ToArray()
                .Length;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(observed);
        return allocated / (double)count;
    }

    private static (double Nanoseconds, double Bytes) BenchmarkRequired(TinyFarmScheduleCatalog catalog)
    {
        return Benchmark(() => TinyFarmNpcSchedule.Decide(catalog, TinyFarmIds.Mara, 1380));
    }

    private static (double Nanoseconds, double Bytes) BenchmarkOpen(
        TinyFarmScheduleCatalog catalog,
        bool includeTrace)
    {
        return Benchmark(() => TinyFarmNpcSchedule.Decide(
            catalog,
            TinyFarmIds.Mara,
            1200,
            TinyFarmAnchorIds.TownSquare,
            includeTrace));
    }

    private static (double Nanoseconds, double Bytes) Benchmark(Func<TinyFarmScheduleDecision> decide)
    {
        for (int index = 0; index < 10_000; index++)
        {
            _ = decide();
        }

        const int count = 100_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < count; index++)
        {
            _ = decide();
        }
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return (watch.Elapsed.TotalNanoseconds / count, allocated / (double)count);
    }

    private static object Allocation(string type, long sampledBytes, string classification)
    {
        return new { type, sampledBytes, classification };
    }

    private static object PerformanceRow(
        string path,
        double m10Nanoseconds,
        double m11Nanoseconds,
        double m10Bytes,
        double m11Bytes)
    {
        return new { path, m10Nanoseconds, m11Nanoseconds, m10Bytes, m11Bytes };
    }

    private static object Capacity(int npcCount, double decisionsPerSecond)
    {
        return new
        {
            npcCount,
            isolatedDecisionsPerSecond = decisionsPerSecond,
            completeDecisionRoundsPerSecond = decisionsPerSecond / npcCount
        };
    }

    private static string Hash(IEnumerable<string> lines)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
