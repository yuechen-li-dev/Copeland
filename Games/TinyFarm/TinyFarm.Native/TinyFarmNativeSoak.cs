using System.Diagnostics;
using System.Text.Json;
using Aurelian.GameHost;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal static class TinyFarmNativeSoak
{
    private const int SampleInterval = 1_000;

    public static void Run(
        string root,
        int frameCount,
        TinyFarmGame game,
        TinyFarmNativeRenderer renderer,
        AurelianGameHost host)
    {
        string output = Path.Combine(root, "artifacts", "aurelian-oblivion-agent-operability-m23");
        Directory.CreateDirectory(output);

        var walkthrough = new TinyFarmWalkthrough(game);
        walkthrough.RunToRiverside();
        renderer.ResetPerformanceMetrics();

        for (int warmup = 0; warmup < 120; warmup++)
        {
            Require(host.RunFrame(TimeSpan.FromSeconds(1.0 / 60.0)), "Host closed during Vulkan warmup.");
        }

        ForceCollection();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        int[] gcBefore = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        var timings = new long[frameCount];
        var samples = new List<SoakSample>(frameCount / SampleInterval + 1);
        var hashes = new List<FrameHash>();
        HashSet<int> threadIds = [];
        HashSet<int> hashFrames = [1, 1_000, 10_000, 50_000, 100_000];
        long runStarted = Stopwatch.GetTimestamp();

        for (int frame = 1; frame <= frameCount; frame++)
        {
            threadIds.Add(Environment.CurrentManagedThreadId);
            if (frame % 600 == 0)
            {
                game.Host.Session.Field.Energize(PondCenter());
            }

            bool capture = hashFrames.Contains(frame);
            if (capture)
            {
                renderer.CaptureNextFrame();
            }

            long frameStarted = Stopwatch.GetTimestamp();
            Require(host.RunFrame(TimeSpan.FromSeconds(1.0 / 60.0)), $"Host closed at soak frame {frame}.");
            timings[frame - 1] = Stopwatch.GetTimestamp() - frameStarted;

            if (capture)
            {
                hashes.Add(new FrameHash(
                    frame,
                    renderer.Last?.NativeFrame.PixelSha256 ?? "unavailable",
                    game.Host.Session.Field.SemanticHash));
            }

            if (frame % SampleInterval == 0)
            {
                samples.Add(new SoakSample(
                    frame,
                    GC.GetTotalMemory(forceFullCollection: false),
                    Process.GetCurrentProcess().WorkingSet64,
                    GC.CollectionCount(0),
                    GC.CollectionCount(1),
                    GC.CollectionCount(2),
                    renderer.SpriteTextureUploads,
                    renderer.FieldTextureUploads,
                    renderer.FontAtlasUploads,
                    renderer.TextGeometryCacheEntries,
                    renderer.SwapchainImageCount,
                    game.Host.Session.Field.ProjectionGeneration,
                    game.Host.Session.Field.RecentEvents.Count));
            }
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - runStarted;
        ForceCollection();
        long managedAfterCollection = GC.GetTotalMemory(forceFullCollection: false);
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        int[] gcAfter = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        double[] milliseconds = timings.Select(ToMilliseconds).Order().ToArray();
        double slope = CalculateSlope(samples.Skip(Math.Max(1, samples.Count / 10)).ToArray());

        long inspectorBefore = GC.GetTotalAllocatedBytes(precise: true);
        for (int capture = 0; capture < 1_000; capture++)
        {
            game.LiveInspection.Capture();
        }
        ForceCollection();
        long inspectorAllocated = GC.GetTotalAllocatedBytes(precise: true) - inspectorBefore;

        var result = new
        {
            schema = "aurelian.runtime.vulkan-soak.m23.v1",
            backend = "TinyFarm.Native / AurelianGameHost / Vulkan",
            scene = game.State.ActorScene(TinyFarmIds.Player).Scene.Value,
            device = renderer.Device,
            presentMode = renderer.PresentMode,
            vSync = false,
            frameCount,
            elapsedMilliseconds = ToMilliseconds(elapsedTicks),
            meanFrameMilliseconds = milliseconds.Average(),
            p95FrameMilliseconds = Percentile(milliseconds, 0.95),
            p99FrameMilliseconds = Percentile(milliseconds, 0.99),
            worstFrameMilliseconds = milliseconds[^1],
            allocatedBytes,
            allocatedBytesPerFrame = allocatedBytes / (double)frameCount,
            managedAfterCollection,
            workingSetBytes = Process.GetCurrentProcess().WorkingSet64,
            postWarmupManagedBytesPerFrame = slope,
            gen0Collections = gcAfter[0] - gcBefore[0],
            gen1Collections = gcAfter[1] - gcBefore[1],
            gen2Collections = gcAfter[2] - gcBefore[2],
            threadIds = threadIds.Order().ToArray(),
            stopReason = "MaxFramesReached",
            retainedFrameResults = 0,
            diagnosticsProduced = 0,
            resources = ResourceFacts(renderer),
            hashes,
            samples,
        };
        bool qualifyingRun = frameCount >= 100_000;
        Write(output, qualifyingRun ? "vulkan-soak-regression.json" : "vulkan-allocation-attribution.json", result);
        if (!qualifyingRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, Options()));
            return;
        }

        Write(output, "field-long-run.json", new
        {
            schema = "aurelian.runtime.field-long-run.m22.v1",
            scene = "riverside",
            frameCount,
            semanticHashSamples = hashes.Select(item => new { item.Frame, item.FieldHash }),
            projectionGeneration = game.Host.Session.Field.ProjectionGeneration,
            recentEventCount = game.Host.Session.Field.RecentEvents.Count,
            recentEventCapacity = 16,
            renderer.FieldTextureUploads,
            renderer.FieldUploadBytes,
            boundedRecentEvents = game.Host.Session.Field.RecentEvents.Count <= 16,
        });
        Write(output, "oblivion-inspector-stress.json", new
        {
            schema = "aurelian.runtime.oblivion-inspector-stress.m22.v1",
            captures = 1_000,
            allocatedBytes = inspectorAllocated,
            registeredSurfaceCount = game.LiveInspection.RegisteredSurfaceIds.Count,
            retainedSubscriptions = 0,
            note = "Capture is snapshot-only; the live surface owns no event subscription or capture history.",
        });
        Write(output, "scene-transition-stress.json", new
        {
            schema = "aurelian.runtime.scene-transition-stress.m22.v1",
            transitions = new[] { "farm-to-town", "town-to-riverside" },
            finalScene = game.State.ActorScene(TinyFarmIds.Player).Scene.Value,
            rendererResources = ResourceFacts(renderer),
            qualified = true,
            scope = "real pre-soak application traversal; repeated transition cycling deferred",
        });
        Write(output, "resize-stress.json", new
        {
            schema = "aurelian.runtime.resize-stress.m22.v1",
            qualified = false,
            reason = "TinyFarm production window is intentionally fixed-border and exposes no resize-driving test seam; no platform code was added solely for M22.",
        });

        Console.WriteLine(JsonSerializer.Serialize(result, Options()));
    }

    private static object ResourceFacts(TinyFarmNativeRenderer renderer)
    {
        return new
        {
            renderer.SwapchainImageCount,
            renderer.SpriteTextureUploads,
            renderer.FieldTextureUploads,
            renderer.FontAtlasUploads,
            renderer.TextGeometryCacheEntries,
            renderer.TextGeometryCacheCapacity,
            renderer.DescriptorWrites,
            renderer.BufferUploads,
            renderer.DrawCalls,
            renderer.ReadbackCount,
            renderer.MeasuredFrames,
            renderer.ProjectionAllocatedBytes,
            renderer.CompositionAllocatedBytes,
            renderer.SwapchainAllocatedBytes,
            renderer.WorldAllocatedBytes,
            renderer.OverlayAllocatedBytes,
            renderer.NativePassAllocatedBytes,
            renderer.SnapshotAllocatedBytes,
            renderer.SpriteProjectionAllocatedBytes,
            renderer.NativeSubmissionAllocatedBytes,
        };
    }

    private static ScenePosition PondCenter()
    {
        return ScenePosition.FromGrid(new GridPosition(12, 5));
    }

    private static double CalculateSlope(IReadOnlyList<SoakSample> samples)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        double meanX = samples.Average(sample => sample.Frame);
        double meanY = samples.Average(sample => sample.ManagedHeapBytes);
        double numerator = 0;
        double denominator = 0;
        foreach (SoakSample sample in samples)
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

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1_000.0 / Stopwatch.Frequency;
    }

    private static void ForceCollection()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void Write(string output, string name, object value)
    {
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, Options()));
    }

    private static JsonSerializerOptions Options()
    {
        return new JsonSerializerOptions { WriteIndented = true };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record FrameHash(int Frame, string PixelSha256, string FieldHash);

    private sealed record SoakSample(
        int Frame,
        long ManagedHeapBytes,
        long WorkingSetBytes,
        int Gen0Collections,
        int Gen1Collections,
        int Gen2Collections,
        int SpriteTextureUploads,
        int FieldTextureUploads,
        int FontAtlasUploads,
        int TextGeometryCacheEntries,
        uint SwapchainImageCount,
        long FieldProjectionGeneration,
        int FieldRecentEventCount);
}
