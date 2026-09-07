using System.Diagnostics;
using System.Text.Json;
using Aurelian.Core.Compositor;
using Aurelian.Core.Engine;
using Aurelian.Core.Engine.Frames;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Rendering.Contracts.Resolved2D;
using Aurelian.Rendering.Raster;
using Aurelian.Runtime.Compositor;
using TinyFarm.Core;

namespace Aurelian.RuntimeSoakM22;

internal static class Program
{
    private const int BaselineFrames = 100_000;
    private const int ProductionFrames = 1_000_000;
    private const int SampleInterval = 1_000;

    public static async Task<int> Main(string[] args)
    {
        string root = FindRoot();
        bool m23 = args.Contains("--m23", StringComparer.Ordinal);
        string output = Path.Combine(
            root,
            "artifacts",
            m23 ? "aurelian-oblivion-agent-operability-m23" : "aurelian-runtime-soak-hardening-m22");
        Directory.CreateDirectory(output);

        RunMetrics baseline = await MeasureFrameLoopAsync(BaselineFrames, captureTranscript: true, failEveryFrame: false);
        RunMetrics production = await MeasureFrameLoopAsync(ProductionFrames, captureTranscript: false, failEveryFrame: false);
        RunMetrics storm = await MeasureFrameLoopAsync(BaselineFrames, captureTranscript: false, failEveryFrame: true);
        RasterMetrics raster = MeasureRaster(10_000);
        HarnessProof harness = await RunHarnessProofAsync();
        CombatMetrics combat = MeasureCombat(10_000);

        Write(output, "baseline-soak.json", new
        {
            schema = "aurelian.runtime.baseline-soak.m22.v1",
            note = "The pre-hardening retention semantics were preserved and measured through the explicit harness after the API split.",
            nullHarness100k = baseline,
        });
        Write(output, "baseline-memory-slope.json", new
        {
            schema = "aurelian.runtime.memory-slope.m22.v1",
            baseline.FrameCount,
            sampleInterval = 10_000,
            samples = DecimateSamples(baseline.Samples, 10_000),
            baseline.PostWarmupManagedBytesPerFrame,
            expectedCause = "explicit full transcript retention",
        });
        Write(output, "frame-retention-proof.json", new
        {
            schema = "aurelian.runtime.retention-proof.m22.v1",
            productionRetainedIterations = production.RetainedIterations,
            harnessRetainedIterations = baseline.RetainedIterations,
            harness,
            productionApi = "AurelianFrameLoop.RunAsync",
            evidenceApi = "AurelianFrameLoop.RunHarnessAsync",
            streamingApi = "AurelianFrameLoop.RunWithSinkAsync",
        });
        Write(output, "null-1m-soak.json", production);
        if (m23)
        {
            Write(output, "null-soak-regression.json", new
            {
                schema = "aurelian.runtime.null-soak-regression.m23.v1",
                baselineGuarantee = "M22 production no-transcript Null loop",
                result = production,
                noTranscriptRetention = production.RetainedIterations == 0,
                boundedMemorySlope = Math.Abs(production.PostWarmupManagedBytesPerFrame) < 1,
            });
        }
        Write(output, "final-memory-slope.json", new
        {
            schema = "aurelian.runtime.memory-slope.m22.v1",
            production.FrameCount,
            sampleInterval = 10_000,
            samples = DecimateSamples(production.Samples, 10_000),
            production.PostWarmupManagedBytesPerFrame,
            production.RetainedIterations,
            bounded = Math.Abs(production.PostWarmupManagedBytesPerFrame) < 1,
        });
        Write(output, "diagnostic-storm.json", new
        {
            schema = "aurelian.runtime.diagnostic-storm.m22.v1",
            storm.FrameCount,
            storm.FramesCompleted,
            storm.Success,
            storm.StopReason,
            storm.AllocatedBytes,
            storm.ManagedBytesAfterCollection,
            storm.Gen0Collections,
            storm.Gen1Collections,
            storm.Gen2Collections,
            storm.RetainedIterations,
            storm.RetainedDiagnostics,
            storm.DroppedDiagnostics,
        });
        Write(output, "raster-soak.json", raster);
        Write(output, "diagnostic-allocation-baseline.json", new
        {
            schema = "aurelian.runtime.diagnostic-allocation.m22.v1",
            source = "pre-hardening-equivalent explicit transcript harness",
            baseline.AllocatedBytes,
            allocatedBytesPerFrame = baseline.AllocatedBytes / (double)baseline.FrameCount,
            baseline.RetainedIterations,
        });
        Write(output, "diagnostic-allocation-after.json", new
        {
            schema = "aurelian.runtime.diagnostic-allocation.m22.v1",
            source = "production no-transcript loop",
            production.AllocatedBytes,
            allocatedBytesPerFrame = production.AllocatedBytes / (double)production.FrameCount,
            production.RetainedIterations,
            note = "Per-frame semantic result allocation remains; retention is removed.",
        });
        Write(output, "performance-before-after.json", new
        {
            schema = "aurelian.runtime.performance-comparison.m22.v1",
            baseline = SelectPerformance(baseline),
            production = SelectPerformance(production),
        });
        Write(output, "thread-affinity-trace.json", new
        {
            schema = "aurelian.runtime.thread-affinity.m22.v1",
            production.EntryThreadId,
            production.InputThreadIds,
            production.DispatchThreadIds,
            production.ExitThreadId,
            assessment = production.InputThreadIds.Length == 1 && production.DispatchThreadIds.Length == 1
                ? "stable in this synchronization-context-free completed-task control"
                : "thread migration observed",
        });
        Write(output, "combat-long-run.json", combat);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            baseline = SelectPerformance(baseline),
            production = SelectPerformance(production),
            storm = new { storm.FrameCount, storm.RetainedDiagnostics, storm.DroppedDiagnostics },
            raster,
            harness,
            combat,
        }, JsonOptions()));
        return production.Success && harness.ExactCount ? 0 : 1;
    }

    private static async Task<RunMetrics> MeasureFrameLoopAsync(
        int frameCount,
        bool captureTranscript,
        bool failEveryFrame)
    {
        ForceCollection();
        int entryThreadId = Environment.CurrentManagedThreadId;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        int[] gcBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        var samples = new List<MemorySample>(frameCount / SampleInterval + 1);
        var timings = new long[frameCount];
        var inputThreads = new HashSet<int>();
        var dispatchThreads = new HashSet<int>();
        var provider = new SoakInputProvider(frameCount, timings, samples, inputThreads);
        var mechanism = new SoakCompositorMechanism(failEveryFrame, dispatchThreads);
        AurelianFramePump pump = StartedPump(mechanism);
        var loop = new AurelianFrameLoop(
            pump,
            provider,
            options: new AurelianFrameLoopOptions(
                MaxFrames: frameCount,
                PresentAfterCompletedFrame: false,
                StopOnFrameFailure: !failEveryFrame,
                MaxRetainedDiagnostics: 64));

        long started = Stopwatch.GetTimestamp();
        AurelianFrameLoopResult completion;
        int retainedIterations;
        if (captureTranscript)
        {
            AurelianFrameLoopHarnessResult harness = await loop.RunHarnessAsync(AurelianFrameId.Zero);
            completion = harness.Completion;
            retainedIterations = harness.Iterations.Count;
        }
        else
        {
            completion = await loop.RunAsync(AurelianFrameId.Zero);
            retainedIterations = 0;
        }
        long elapsedTicks = Stopwatch.GetTimestamp() - started;
        timings[^1] = Math.Max(1, Stopwatch.GetTimestamp() - provider.LastFrameStarted);
        ForceCollection();

        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        int[] gcAfter = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        double[] milliseconds = timings.Select(StopwatchTicksToMilliseconds).Order().ToArray();
        double slope = CalculateSlope(samples.Skip(Math.Max(1, samples.Count / 10)).ToArray());

        return new RunMetrics(
            Schema: "aurelian.runtime.soak.m22.v1",
            Backend: "null-control",
            FrameCount: completion.FramesAttempted,
            FramesCompleted: completion.FramesCompleted,
            Success: failEveryFrame ? completion.Status == AurelianFrameLoopStatus.Completed : completion.Success,
            StopReason: completion.StopReason.ToString(),
            ElapsedMilliseconds: StopwatchTicksToMilliseconds(elapsedTicks),
            MeanFrameMilliseconds: milliseconds.Average(),
            P95FrameMilliseconds: Percentile(milliseconds, 0.95),
            P99FrameMilliseconds: Percentile(milliseconds, 0.99),
            WorstFrameMilliseconds: milliseconds[^1],
            AllocatedBytes: allocatedBytes,
            ManagedBytesAfterCollection: GC.GetTotalMemory(forceFullCollection: false),
            WorkingSetBytes: Process.GetCurrentProcess().WorkingSet64,
            Gen0Collections: gcAfter[0] - gcBefore[0],
            Gen1Collections: gcAfter[1] - gcBefore[1],
            Gen2Collections: gcAfter[2] - gcBefore[2],
            RetainedIterations: retainedIterations,
            RetainedDiagnostics: completion.Diagnostics.Count,
            DroppedDiagnostics: completion.DroppedDiagnosticCount,
            EntryThreadId: entryThreadId,
            InputThreadIds: inputThreads.Order().ToArray(),
            DispatchThreadIds: dispatchThreads.Order().ToArray(),
            ExitThreadId: Environment.CurrentManagedThreadId,
            PostWarmupManagedBytesPerFrame: slope,
            Samples: samples);
    }

    private static RasterMetrics MeasureRaster(int frameCount)
    {
        var renderer = new AurelianCpuRasterRenderer();
        var plan = new Resolved2DPlan(
            new Resolved2DViewport(64, 64),
            [new FillRectangleOperation(
                "stable",
                new Resolved2DRectangle(4, 4, 56, 56),
                new Resolved2DRgbaColor(30, 120, 180, 255))]);
        renderer.Render(plan);
        ForceCollection();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long started = Stopwatch.GetTimestamp();
        uint checksum = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            RasterFrame rasterFrame = renderer.Render(plan);
            checksum ^= rasterFrame.Surface.Pixels[0].R;
        }

        return new RasterMetrics(
            "aurelian.runtime.raster-soak.m22.v1",
            frameCount,
            StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - started),
            GC.GetTotalAllocatedBytes(true) - allocatedBefore,
            checksum,
            "RasterFrame owns a fresh immutable surface per render; output is discarded each frame.");
    }

    private static async Task<HarnessProof> RunHarnessProofAsync()
    {
        var loop = new AurelianFrameLoop(
            StartedPump(new SoakCompositorMechanism(false, [])),
            new SoakInputProvider(500, new long[500], [], []),
            options: new AurelianFrameLoopOptions(MaxFrames: 500, PresentAfterCompletedFrame: false));
        AurelianFrameLoopHarnessResult result = await loop.RunHarnessAsync(AurelianFrameId.Zero);
        return new HarnessProof(result.FramesAttempted, result.Iterations.Count, result.StopReason.ToString(), result.Iterations.Count == 500);
    }

    private static CombatMetrics MeasureCombat(int actionCount)
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        ForceCollection();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        int accepted = 0;
        int completed = 0;
        int maximumHitIds = 0;
        for (int action = 0; action < actionCount; action++)
        {
            var host = new TinyFarmSimulationHost(
                new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions),
                definitions,
                TinyFarmSimulationMode.Playing);
            TinyFarmStepResult start = host.ExecuteIntent(new AttackIntent(TinyFarmIds.DungeonSlime));
            if (start.Results.Single().Status == IntentResultStatus.Accepted)
            {
                accepted++;
            }
            host.AdvanceHostTime(TimeSpan.FromMilliseconds(400));
            if (!host.Session.HasActiveCombat && host.Session.CombatInspection.Phase == "Complete")
            {
                completed++;
            }
            maximumHitIds = Math.Max(maximumHitIds, host.Session.CombatInspection.HitIds.Count);
        }

        ForceCollection();
        return new CombatMetrics(
            "aurelian.runtime.combat-long-run.m22.v1",
            actionCount,
            accepted,
            completed,
            maximumHitIds,
            GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
            StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - started),
            completed == actionCount && maximumHitIds <= 1);
    }

    private static AurelianFramePump StartedPump(ICompositorMechanism mechanism)
    {
        var engine = new AurelianEngine();
        if (!engine.Start().Success)
        {
            throw new InvalidOperationException("Soak engine failed to start.");
        }

        return new AurelianFramePump(engine, new CompositorActuationBridge(mechanism));
    }

    private static object SelectPerformance(RunMetrics metrics)
    {
        return new
        {
            metrics.FrameCount,
            metrics.ElapsedMilliseconds,
            metrics.MeanFrameMilliseconds,
            metrics.P95FrameMilliseconds,
            metrics.P99FrameMilliseconds,
            metrics.WorstFrameMilliseconds,
            metrics.AllocatedBytes,
            allocatedBytesPerFrame = metrics.AllocatedBytes / (double)metrics.FrameCount,
            metrics.RetainedIterations,
            metrics.PostWarmupManagedBytesPerFrame,
        };
    }

    private static IReadOnlyList<MemorySample> DecimateSamples(
        IReadOnlyList<MemorySample> samples,
        int interval)
    {
        return samples
            .Where(sample => sample.Frame % interval == 0)
            .ToArray();
    }

    private static double CalculateSlope(IReadOnlyList<MemorySample> samples)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        double meanX = samples.Average(sample => sample.Frame);
        double meanY = samples.Average(sample => sample.ManagedHeapBytes);
        double numerator = 0;
        double denominator = 0;
        foreach (MemorySample sample in samples)
        {
            double x = sample.Frame - meanX;
            numerator += x * (sample.ManagedHeapBytes - meanY);
            denominator += x * x;
        }

        return denominator == 0 ? 0 : numerator / denominator;
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static double StopwatchTicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static void ForceCollection()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void Write(string output, string name, object value)
    {
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions { WriteIndented = true };
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find Aurelian.slnx.");
    }

    private sealed class SoakInputProvider : IAurelianFrameInputProvider
    {
        private readonly int frameCount;
        private readonly long[] timings;
        private readonly List<MemorySample> samples;
        private readonly HashSet<int> threadIds;

        public SoakInputProvider(
            int frameCount,
            long[] timings,
            List<MemorySample> samples,
            HashSet<int> threadIds)
        {
            this.frameCount = frameCount;
            this.timings = timings;
            this.samples = samples;
            this.threadIds = threadIds;
        }

        public long LastFrameStarted { get; private set; } = Stopwatch.GetTimestamp();

        public ValueTask<AurelianFrameInput?> GetNextFrameInputAsync(
            AurelianFrameId frameId,
            CancellationToken cancellationToken = default)
        {
            threadIds.Add(Environment.CurrentManagedThreadId);
            int frame = checked((int)frameId.Value);
            long now = Stopwatch.GetTimestamp();
            if (frame > 0 && frame <= timings.Length)
            {
                timings[frame - 1] = Math.Max(1, now - LastFrameStarted);
            }
            LastFrameStarted = now;

            if (frame > 0 && frame % SampleInterval == 0)
            {
                samples.Add(new MemorySample(
                    frame,
                    GC.GetTotalMemory(forceFullCollection: false),
                    Process.GetCurrentProcess().WorkingSet64,
                    GC.CollectionCount(0),
                    GC.CollectionCount(1),
                    GC.CollectionCount(2)));
            }

            if (frame >= frameCount)
            {
                return ValueTask.FromResult<AurelianFrameInput?>(null);
            }

            var output = new PlantOutputRef(0, frameId.Value, "null");
            var readiness = new PlantOutputReadiness(output, PlantOutputReadinessStatus.Ready, frameId.Value);
            var target = new PresentationTargetRef(0, 0, frameId.Value);
            var facts = new CompositorFrameFacts(frameId.Value, [readiness], CompositorDiagnostics.Empty);
            var required = new RequiredPlantOutputSet(frameId.Value, CompositorPolicyKind.Passthrough, [output]);
            return ValueTask.FromResult<AurelianFrameInput?>(new AurelianFrameInput(
                frameId,
                new CompositorPolicyFacts(facts, required, target, CompositorPolicyKind.Passthrough)));
        }
    }

    private sealed class SoakCompositorMechanism : ICompositorMechanism
    {
        private readonly bool fail;
        private readonly HashSet<int> threadIds;

        public SoakCompositorMechanism(bool fail, HashSet<int> threadIds)
        {
            this.fail = fail;
            this.threadIds = threadIds;
        }

        public Task<CompositorDispatchResult> DispatchAsync(
            CompositorDispatchRequest request,
            CancellationToken cancellationToken = default)
        {
            threadIds.Add(Environment.CurrentManagedThreadId);
            if (fail)
            {
                return Task.FromResult(new CompositorDispatchResult(
                    CompositorDispatchStatus.Failed,
                    request.FrameId,
                    request.Policy,
                    request.Target,
                    CompositorDiagnostics.Empty,
                    [new CompositorDispatchDiagnostic("M22-STORM", CompositorDispatchDiagnosticSeverity.Error, "Controlled recurring failure.")]));
            }

            return Task.FromResult(new CompositorDispatchResult(
                CompositorDispatchStatus.Dispatched,
                request.FrameId,
                request.Policy,
                request.Target,
                CompositorDiagnostics.Empty,
                []));
        }
    }

    private sealed record MemorySample(
        int Frame,
        long ManagedHeapBytes,
        long WorkingSetBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections);

    private sealed record RunMetrics(
        string Schema,
        string Backend,
        int FrameCount,
        int FramesCompleted,
        bool Success,
        string StopReason,
        double ElapsedMilliseconds,
        double MeanFrameMilliseconds,
        double P95FrameMilliseconds,
        double P99FrameMilliseconds,
        double WorstFrameMilliseconds,
        long AllocatedBytes,
        long ManagedBytesAfterCollection,
        long WorkingSetBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        int RetainedIterations,
        int RetainedDiagnostics,
        int DroppedDiagnostics,
        int EntryThreadId,
        int[] InputThreadIds,
        int[] DispatchThreadIds,
        int ExitThreadId,
        double PostWarmupManagedBytesPerFrame,
        IReadOnlyList<MemorySample> Samples);

    private sealed record RasterMetrics(
        string Schema,
        int FrameCount,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        uint Checksum,
        string RetentionPolicy);

    private sealed record HarnessProof(
        int FramesAttempted,
        int RetainedIterations,
        string StopReason,
        bool ExactCount);

    private sealed record CombatMetrics(
        string Schema,
        int ActionCount,
        int AcceptedCount,
        int CompletedCount,
        int MaximumHitIdsPerAction,
        long AllocatedBytes,
        double ElapsedMilliseconds,
        bool Bounded);
}
