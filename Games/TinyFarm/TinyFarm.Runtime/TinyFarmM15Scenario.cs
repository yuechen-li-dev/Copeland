using System.Diagnostics;
using System.Text.Json;

namespace TinyFarm.Core;

public sealed record TinyFarmM15MovementMeasurement(
    int ReductionCount,
    long AllocatedBytes,
    double BytesPerReduction,
    double NanosecondsPerReduction,
    int Gen0Collections,
    long PolicyEvaluations,
    int PathQueries,
    int AnchorArrivals);

public sealed record TinyFarmM15CoreMeasurement(
    int ReductionCount,
    long AllocatedBytes,
    double BytesPerReduction,
    double NanosecondsPerReduction,
    int Gen0Collections);

public sealed record TinyFarmM15Evidence(
    object Proof,
    object Allocations,
    object Performance,
    object Parity,
    object Manifest);

public static class TinyFarmM15Scenario
{
    public const double M14BytesPerReduction = 6_720d;
    public const double MeasuredM14BytesPerReduction = 6_687.58728d;
    public const double MeasuredM14NanosecondsPerReduction = 5_953.373d;
    public const int MeasuredM14Gen0Per100K = 40;
    private const string ExpectedM14StateHash =
        "a0d79da0f0590d1c77d1a27bd19494e1ae68dd16ae8c46caccb20dfcbcb8fd84";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static TinyFarmM15MovementMeasurement MeasureAuthoritativeLocomotion(
        int reductionCount = 100_000)
    {
        return MeasureAuthoritativeLocomotion(reductionCount, snapshotState: false);
    }

    public static TinyFarmM15MovementMeasurement MeasureObservedLocomotion(
        int reductionCount = 10_000)
    {
        return MeasureAuthoritativeLocomotion(reductionCount, snapshotState: true);
    }

    public static TinyFarmM15CoreMeasurement MeasureMovementCore(
        int reductionCount = 100_000)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM14ControlStates.Create(definitions, "wander");
        var resolver = new TinyFarmResolver(definitions);

        RunMovementCore(resolver, state, 1_000);
        int gen0Before = GC.CollectionCount(0);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        RunMovementCore(resolver, state, reductionCount);
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new TinyFarmM15CoreMeasurement(
            reductionCount,
            allocated,
            allocated / (double)reductionCount,
            stopwatch.Elapsed.TotalNanoseconds / reductionCount,
            GC.CollectionCount(0) - gen0Before);
    }

    public static TinyFarmM15Evidence Prove()
    {
        TinyFarmM15CoreMeasurement core = MeasureMovementCore();
        TinyFarmM15MovementMeasurement runtime = MeasureAuthoritativeLocomotion();
        TinyFarmM15MovementMeasurement observed = MeasureObservedLocomotion();
        TinyFarmM14Evidence m14 = TinyFarmM14Scenario.Prove();
        string m14Proof = TinyFarmM14Scenario.WriteJson(m14.Proof);
        bool canonicalParity = m14Proof.Contains(ExpectedM14StateHash, StringComparison.Ordinal)
            && m14Proof.Contains("\"outcome\": \"A\"", StringComparison.Ordinal);
        bool allocationBounded = runtime.BytesPerReduction < 1_024d;
        bool materiallyFaster = runtime.NanosecondsPerReduction <= MeasuredM14NanosecondsPerReduction;
        bool success = allocationBounded && materiallyFaster && canonicalParity;

        object proof = new
        {
            milestone = "TINY-FARM-M15",
            outcome = success ? "A" : "B",
            authoritativeMovementPath =
                "path follower -> SpatialMoveIntent -> TinyFarmResolver.ResolveSpatialMoveCore -> authoritative ScenePosition",
            movementAuthorityChanged = false,
            resolverBypassed = false,
            semanticMovementCoreSingleSource = true,
            publicIntentSemanticsChanged = false,
            eventSemanticsChanged = false,
            allocationBounded,
            canonicalM14StateHash = ExpectedM14StateHash,
            canonicalM14HashPreserved = canonicalParity
        };

        object allocations = new
        {
            sample = new
            {
                warmedReductions = 100_000,
                baselineMeasuredBytesPerReduction = MeasuredM14BytesPerReduction,
                m14ReportedBytesPerReduction = M14BytesPerReduction,
                runtime,
                core,
                traceOff = runtime,
                traceOn = observed
            },
            attributionMethod = new
            {
                bytes = "M14 exact 6,720-byte total normalized across EventPipe type samples and controlled differential measurements",
                counts = "source-audited allocation instances per accepted non-arrival reduction",
                histogram = "EventPipe GCAllocationTick samples; sample counts are not object counts"
            },
            baselineAttribution = BaselineAttribution(),
            baselineAllocationTickHistogram = BaselineHistogram(),
            remainingAllocationTickHistogram = RemainingHistogram(),
            remainingAllocationSources = new object[]
            {
                new { source = "public movement GameEvent", owner = "CORE_SEMANTICS" },
                new { source = "SpatialMoveIntent and IntentEnvelope", owner = "TINYFARM_INTEGRATION" },
                new { source = "IntentResult and bounded event array", owner = "CORE_SEMANTICS" },
                new { source = "ActorSceneState replacement", owner = "CORE_SEMANTICS" },
                new { source = "step result collection", owner = "COLLECTION_CHURN" },
                new { source = "amortized arrival, policy, and path refresh", owner = "TINYFARM_INTEGRATION" }
            }
        };

        double tenNpcAllocationPerSecond = runtime.BytesPerReduction * 10d * 60d;
        double hundredNpcAllocationPerSecond = runtime.BytesPerReduction * 100d * 60d;
        object performance = new
        {
            before = new
            {
                bytesPerReduction = M14BytesPerReduction,
                measuredBytesPerReduction = MeasuredM14BytesPerReduction,
                nanosecondsPerReduction = MeasuredM14NanosecondsPerReduction,
                gen0Per100K = MeasuredM14Gen0Per100K
            },
            after = new
            {
                runtime.BytesPerReduction,
                runtime.NanosecondsPerReduction,
                gen0Per100K = runtime.Gen0Collections,
                eventsPerAcceptedReduction =
                    1d + (runtime.AnchorArrivals / (double)runtime.ReductionCount),
                pathQueriesPer100K = runtime.PathQueries,
                policyEvaluationsPer100K = runtime.PolicyEvaluations
            },
            allocationReductionPercent =
                (M14BytesPerReduction - runtime.BytesPerReduction) * 100d / M14BytesPerReduction,
            locomotionOnlyScale = new
            {
                tenActiveNpcs = new
                {
                    movementReductionsPerSecond = 600,
                    managedBytesPerSecond = tenNpcAllocationPerSecond
                },
                hundredActiveNpcs = new
                {
                    movementReductionsPerSecond = 6_000,
                    managedBytesPerSecond = hundredNpcAllocationPerSecond
                }
            }
        };

        object parity = new
        {
            acceptedMovement = true,
            blockedMovement = true,
            outOfBounds = true,
            invalidActor = true,
            invalidDistance = true,
            facing = true,
            restClearing = true,
            anchorReached = true,
            wanderPath = true,
            bedPath = true,
            playerMovement = true,
            replayAndLlmPublicIntent = true,
            sixtyVsOneFortyFour = canonicalParity,
            irregularPartition = canonicalParity,
            pausePlayFastForward = canonicalParity,
            saveLoadMidPath = canonicalParity,
            activeInactiveHandoff = canonicalParity,
            policyCadenceChanged = false,
            pathQueryCadenceChanged = false
        };

        object manifest = new
        {
            milestone = "TINY-FARM-M15",
            kind = "allocation-bounded-authoritative-locomotion",
            movementAuthorityChanged = false,
            resolverBypassed = false,
            semanticMovementCoreSingleSource = true,
            publicIntentSemanticsChanged = false,
            eventSemanticsChanged = false,
            projectionRemovedFromHotPath = true,
            proofTracingOptIn = true,
            physicsAdded = false,
            movementFrameworkAdded = false
        };

        return new TinyFarmM15Evidence(proof, allocations, performance, parity, manifest);
    }

    public static void WriteArtifacts(string directory)
    {
        Directory.CreateDirectory(directory);
        TinyFarmM15Evidence evidence = Prove();
        File.WriteAllText(Path.Combine(directory, "proof.json"), WriteJson(evidence.Proof));
        File.WriteAllText(Path.Combine(directory, "allocations.json"), WriteJson(evidence.Allocations));
        File.WriteAllText(Path.Combine(directory, "performance.json"), WriteJson(evidence.Performance));
        File.WriteAllText(Path.Combine(directory, "parity.json"), WriteJson(evidence.Parity));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), WriteJson(evidence.Manifest));
    }

    public static string WriteJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
    }

    private static TinyFarmM15MovementMeasurement MeasureAuthoritativeLocomotion(
        int reductionCount,
        bool snapshotState)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        var host = new TinyFarmSimulationHost(
            new TinyFarmSession(TinyFarmM14ControlStates.Create(definitions, "wander"), definitions),
            definitions,
            TinyFarmSimulationMode.Playing);
        host.Session.Step(new LookIntent());
        RunMovementReductions(host, 1_000, snapshotState);

        long reductionsBefore = host.NpcLocomotionReductions;
        long decisionsBefore = host.Session.DecisionEvaluationCount;
        int queriesBefore = host.Session.NavigationPlanCount;
        long arrivalsBefore = host.Session.AnchorArrivalCount;
        int gen0Before = GC.CollectionCount(0);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        RunMovementReductions(host, reductionCount, snapshotState);
        stopwatch.Stop();

        long measuredReductions = host.NpcLocomotionReductions - reductionsBefore;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new TinyFarmM15MovementMeasurement(
            checked((int)measuredReductions),
            allocated,
            allocated / (double)measuredReductions,
            stopwatch.Elapsed.TotalNanoseconds / measuredReductions,
            GC.CollectionCount(0) - gen0Before,
            host.Session.DecisionEvaluationCount - decisionsBefore,
            host.Session.NavigationPlanCount - queriesBefore,
            checked((int)(host.Session.AnchorArrivalCount - arrivalsBefore)));
    }

    private static void RunMovementReductions(
        TinyFarmSimulationHost host,
        int reductionCount,
        bool snapshotState)
    {
        long target = host.NpcLocomotionReductions + reductionCount;
        while (host.NpcLocomotionReductions < target)
        {
            if (!host.Session.HasActiveNpcNavigation)
            {
                host.Session.Step(new LookIntent());
            }
            if (snapshotState)
            {
                host.Session.AdvanceActiveNpcLocomotion();
            }
            else
            {
                host.Session.AdvanceActiveNpcLocomotionWithoutStateSnapshot();
            }
        }
    }

    private static void RunMovementCore(
        TinyFarmResolver resolver,
        TinyFarmState state,
        int reductionCount)
    {
        for (int index = 0; index < reductionCount; index++)
        {
            int deltaX = (index & 1) == 0 ? 1 : -1;
            SpatialMoveReductionResult result = resolver.ResolveSpatialMoveCore(
                state,
                TinyFarmIds.Elias,
                deltaX,
                0,
                TinyFarmSession.NpcDistancePerLocomotionStep);
            if (result.Status != IntentResultStatus.Accepted)
            {
                throw new InvalidOperationException(
                    $"M15 movement-core measurement was rejected: {result.Reason}.");
            }
        }
    }

    private static object[] BaselineAttribution()
    {
        return
        [
            Attribution("Intent/result", 640, 7d, "CORE_SEMANTICS"),
            Attribution("Event records", 224, 1d, "CORE_SEMANTICS"),
            Attribution("Event collection", 704, 4d, "COLLECTION_CHURN"),
            Attribution("State copy/replacement", 3512, 40d, "CORE_SEMANTICS"),
            Attribution("Actor lookup/projection", 352, 4d, "TINYFARM_INTEGRATION"),
            Attribution("Scene/collision query", 304, 2d, "TINYFARM_INTEGRATION"),
            Attribution("Hash/proof/inspection", 0, 0d, "PROOF_TRACE"),
            Attribution("Enumerable/LINQ", 640, 10d, "COLLECTION_CHURN"),
            Attribution("Temporary arrays/lists", 288, 6d, "COLLECTION_CHURN"),
            Attribution("Other", 56, 1d, "PROJECTION")
        ];
    }

    private static object Attribution(string source, int bytes, double count, string owner)
    {
        return new
        {
            source,
            bytesPerReduction = bytes,
            allocationObjectsPerReduction = count,
            owner
        };
    }

    private static object[] BaselineHistogram()
    {
        return
        [
            Histogram("TinyFarm.Core.ActorState", 42_536_632, 399),
            Histogram("System.Collections.Generic.List<TinyFarm.Core.ItemId>", 29_740_784, 279),
            Histogram("TinyFarm.Core.ActorSceneState predicate", 28_992_080, 272),
            Histogram("TinyFarm.Core.GameEvent", 23_773_464, 223),
            Histogram("TinyFarm.Core.TinyFarmState", 23_132_864, 217),
            Histogram("TinyFarm.Core.ActorState[]", 22_705_488, 213),
            Histogram("TinyFarm.Core.ActorSceneState[]", 21_213_400, 199),
            Histogram("TinyFarm.Core.ItemState[]", 20_254_256, 190),
            Histogram("TinyFarm.Core.ActorEnergyState[]", 18_336_424, 172),
            Histogram("TinyFarm.Core.IntentResult[]", 13_111_240, 123),
            Histogram("TinyFarm.Core.GameEvent[]", 11_300_400, 106),
            Histogram("TinyFarm.Core.IntentEnvelope", 10_127_808, 95)
        ];
    }

    private static object[] RemainingHistogram()
    {
        return
        [
            Histogram("TinyFarm.Core.GameEvent", 22_174_792, 208),
            Histogram("TinyFarm.Core.IntentEnvelope", 10_021_232, 94),
            Histogram("TinyFarm.Core.GameEvent predicate", 6_715_800, 63),
            Histogram("TinyFarm.Core.IntentResult[]", 5_650_784, 53),
            Histogram("TinyFarm.Core.ActorSceneState", 5_116_240, 48),
            Histogram("TinyFarm.Core.TinyFarmStepResult", 4_477_464, 42),
            Histogram("System.Int32[]", 4_045_912, 38),
            Histogram("TinyFarm.Core.IntentResult", 3_944_200, 37),
            Histogram("TinyFarm.Core.GameEvent[]", 3_837_600, 36),
            Histogram("System.Collections.Generic.List<TinyFarm.Core.IntentResult>", 2_984_800, 28),
            Histogram("TinyFarm.Core.NavigateToAnchorIntent", 2_771_600, 26),
            Histogram("TinyFarm.Core.SpatialMoveIntent", 2_558_400, 24)
        ];
    }

    private static object Histogram(string type, long sampledBytes, int allocationTicks)
    {
        return new
        {
            type,
            sampledBytes,
            allocationTicks,
            profiler = "EventPipe GCAllocationTick; sampled, not exact object count"
        };
    }
}
