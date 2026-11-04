using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private sealed record TlasTiming(string Mode, int Width, int Height, int Frames,
        double MedianCpuPrepareSubmitMilliseconds, double MedianSlotWaitMilliseconds,
        double MedianGpuFrameMilliseconds, double WallFramesPerSecond, int TopLevelBuilds,
        int TopLevelUpdates, int SnapshotAllocations, int SynchronousBuildSubmissions);

    public static void RunTlas(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Retained TLAS updates"));
        Require(initialized.Success, "TLAS plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("tlas-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var quality = new List<object>();
        foreach (var mode in new[] { LocalLightSamplingMode3D.Exact, LocalLightSamplingMode3D.Importance,
            LocalLightSamplingMode3D.SpatiotemporalReservoir })
        {
            using var updateTarget = new VulkanNativeFrameTarget(plant, Width, Height);
            using var rebuildTarget = new VulkanNativeFrameTarget(plant, Width, Height);
            using var update = TlasRenderer(plant, assets, updateTarget, SceneAccelerationUpdateMode3D.Automatic, mode);
            using var rebuild = TlasRenderer(plant, assets, rebuildTarget, SceneAccelerationUpdateMode3D.Rebuild, mode);
            NativeInstanceSet3D? previous = null;
            byte[]? first = null;
            int changedFrames = 0;
            double maximumError = 0;
            double maximumReferenceError = 0;
            for (int frame = 0; frame < 24; frame++)
            {
                var scene = TlasMotionScene(box, sphere, previous, frame);
                var camera = Camera with { Eye = Camera.Eye + new Vector3(MathF.Sin(frame * .2f) * .3f, 0, 0) };
                byte[] actual = update.Render(scene, RestirMatrix(camera), camera.Eye, Clear, capture: true).Pixels!;
                byte[] control = rebuild.Render(scene, RestirMatrix(camera), camera.Eye, Clear, capture: true).Pixels!;
                double error = Error(actual, control);
                maximumError = Math.Max(maximumError, error);
                Require(actual.SequenceEqual(control), "TLAS update differs from rebuilt current visibility: " + error);
                first ??= actual;
                if (!actual.SequenceEqual(first))
                {
                    changedFrames++;
                }
                var statistics = update.RaySceneStatistics!;
                Require(statistics.MeshBuilds == 2 && statistics.TopLevelBuilds == 1 && statistics.TopLevelUpdates == frame,
                    "Motion did not use exactly one initial TLAS build followed by inline updates.");
                Require(statistics.SnapshotAllocations == 8 && statistics.SynchronousBuildSubmissions == 3,
                    "Motion allocated acceleration buffers or issued a separate synchronous build submission.");
                if (mode == LocalLightSamplingMode3D.Exact && frame % 8 == 0)
                {
                    var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, Lights(), camera);
                    byte[] expected = VulkanReferenceOutput3D.ToneMap(plant, assets.Shader("ToneMap3D.v.ts"), reference);
                    double referenceError = Error(actual, expected);
                    maximumReferenceError = Math.Max(maximumReferenceError, referenceError);
                    Require(referenceError < .002, "Updated shadows disagree with the independent current-frame reference.");
                    NativeGameGraphics.WritePng(Path.Combine(output, $"reference-{frame}.png"), Width, Height, expected);
                }
                previous = (NativeInstanceSet3D)scene.Instances;
                if (frame == 23)
                {
                    NativeGameGraphics.WritePng(Path.Combine(output, mode + "-updated.png"), Width, Height, actual);
                    NativeGameGraphics.WritePng(Path.Combine(output, mode + "-rebuilt.png"), Width, Height, control);
                }
            }
            Require(changedFrames > 16, "The motion sequence did not exercise changing current visibility.");
            quality.Add(new { Mode = mode.ToString(), Frames = 24, MaximumRebuildDisplayMae = maximumError,
                MaximumReferenceDisplayMae = maximumReferenceError, ChangedFrames = changedFrames,
                Updated = update.RaySceneStatistics, Rebuilt = rebuild.RaySceneStatistics });
            Console.WriteLine($"TLAS {mode}: 24 byte-identical updated/rebuilt frames; {update.RaySceneStatistics}");
            CheckTlasRebuildBoundaries(update, box, sphere);
        }
        object queued = CheckQueuedTlasUpdates(plant, assets, box);
        var measurements = new List<TlasTiming>();
        foreach (var mode in new[] { SceneAccelerationUpdateMode3D.Rebuild, SceneAccelerationUpdateMode3D.Automatic })
        {
            measurements.Add(MeasureTlas(plant, assets, box, sphere, mode));
        }
        Require(measurements[1].MedianCpuPrepareSubmitMilliseconds < measurements[0].MedianCpuPrepareSubmitMilliseconds * .85,
            "TLAS updates did not materially reduce full-path moving-frame CPU cost.");
        Require(measurements[1].WallFramesPerSecond > measurements[0].WallFramesPerSecond * 1.1,
            "TLAS updates did not materially improve full-path moving-frame throughput.");
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Quality = quality,
            Measurements = measurements,
            QueuedFrames = queued,
            Scope = "Opaque retained rigid instance motion. Shared mesh/analytic BLAS; frame-ordered in-place TLAS updates. Composition changes rebuild. No deforming BLAS refit.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_TLAS_PASSED " + output);
    }

    private static object CheckQueuedTlasUpdates(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box)
    {
        // The direct retained triangle path avoids unrelated visibility/material
        // re-admission uploads. It can queue two moving frames without a build wait.
        using var target = new VulkanNativeFrameTarget(plant, 1280, 720);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, rayShadows: true);
        renderer.EnableBindlessMaterials = false;
        renderer.EnableRayShadows = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Settings();
        renderer.LocalLights = Lights();
        NativeInstanceSet3D? previous = null;
        int waits = 0;
        Native3DScene? last = null;
        for (int frame = 0; frame <= 48; frame++)
        {
            var source = TlasMotionScene(box, box, null, frame);
            var items = source.Instances.ToList();
            for (int index = 0; index < 1024; index++)
            {
                var position = new Vector3((index % 32) * .22f - 3.5f + .04f * MathF.Sin(frame * .17f + index),
                    .12f + .03f * MathF.Sin(frame * .21f + index), (index / 32) * .22f - 3.5f);
                items.Add(new("grid-" + index, box, source.Instances[0].Material,
                    Matrix4x4.CreateScale(.045f, .12f, .045f) * Matrix4x4.CreateTranslation(position)));
            }
            last = source with { Instances = new NativeInstanceSet3D(items, previous) };
            var result = renderer.Render(last, Matrix(1280, 720), Camera.Eye, Clear, capture: frame == 48);
            if (result.CpuTimings!.SlotWaitMilliseconds > .1)
            {
                waits++;
            }
            previous = (NativeInstanceSet3D)last.Instances;
            if (frame == 48)
            {
                using var controlTarget = new VulkanNativeFrameTarget(plant, 1280, 720);
                using var control = RenderingBindlessProof.CreateRenderer(plant, assets, controlTarget, rayShadows: true);
                control.EnableBindlessMaterials = false;
                control.EnableRayShadows = true;
                control.Settings = Settings();
                control.LocalLights = Lights();
                control.RaySceneUpdateMode = SceneAccelerationUpdateMode3D.Rebuild;
                byte[] expected = control.Render(last, Matrix(1280, 720), Camera.Eye, Clear, capture: true).Pixels!;
                Require(result.Pixels!.SequenceEqual(expected), "Queued TLAS updates consumed stale per-frame instance data.");
            }
        }
        var statistics = renderer.RaySceneStatistics!;
        Require(statistics.TopLevelBuilds == 1 && statistics.TopLevelUpdates == 48
            && statistics.SynchronousBuildSubmissions == 2 && statistics.SnapshotAllocations == 8 && waits > 8,
            "Queued motion did not exercise frame-slot reuse without acceleration build submissions.");
        Console.WriteLine($"TLAS queued frames: {statistics.TopLevelUpdates} updates, {waits} frame-slot waits, final image byte-identical.");
        return new { Frames = 49, SlotWaits = waits, Statistics = statistics, FinalImageMatchesRebuild = true };
    }

    private static VulkanSolid3DRenderer TlasRenderer(AurelianVulkanPlant plant, GameAssets assets,
        VulkanNativeFrameTarget target, SceneAccelerationUpdateMode3D maintenance, LocalLightSamplingMode3D sampling)
    {
        var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.EnableRayShadows = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Settings();
        renderer.LocalLights = Lights();
        renderer.RaySceneUpdateMode = maintenance;
        renderer.LocalLightSampling = RestirOptions(sampling, (int)(target.Width * target.Height));
        return renderer;
    }

    private static Native3DScene TlasMotionScene(NativeMesh3D box, NativeMesh3D sphere, NativeInstanceSet3D? previous, int frame)
    {
        var source = Scene(box, sphere, 0);
        var items = source.Instances.Select(instance =>
        {
            if (instance.Identity == "moving")
            {
                float reflection = 1;
                if (frame % 6 >= 3 && sphere.PrimitiveKind != AnalyticPrimitiveKind3D.Triangles)
                {
                    reflection = -1;
                }
                var transform = Matrix4x4.CreateScale(reflection, 1, .8f + MathF.Cos(frame * .23f) * .2f)
                    * Matrix4x4.CreateRotationY(frame * .07f)
                    * Matrix4x4.CreateTranslation(MathF.Sin(frame * .27f), .1f * MathF.Sin(frame * .3f), 0);
                return instance with { Transform = instance.Transform * transform };
            }
            if (instance.Identity == "column-2" || instance.Identity == "off-camera")
            {
                return instance with
                {
                    Transform = instance.Transform * Matrix4x4.CreateTranslation(.6f * MathF.Sin(frame * .35f), 0, 0),
                    RayMask = frame % 8 < 4 ? uint.MaxValue : 0,
                };
            }
            return instance;
        });
        return source with { Instances = new NativeInstanceSet3D(items, previous) };
    }

    private static void CheckTlasRebuildBoundaries(VulkanSolid3DRenderer renderer, NativeMesh3D box, NativeMesh3D sphere)
    {
        // A new static snapshot deliberately breaks motion correspondence.
        var source = Scene(box, sphere, 0);
        int builds = renderer.RaySceneStatistics!.TopLevelBuilds;
        renderer.Render(source, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
        Require(renderer.RaySceneStatistics!.TopLevelBuilds == builds + 1, "Static replacement did not rebuild TLAS.");
        var fewer = source with { Instances = new NativeInstanceSet3D(source.Instances.Skip(1)) };
        renderer.Render(fewer, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
        Require(renderer.RaySceneStatistics!.TopLevelBuilds == builds + 2, "Changed instance count did not rebuild TLAS.");
        var differentMesh = Box();
        var replacement = source with
        {
            Instances = new NativeInstanceSet3D(source.Instances.Select(instance => instance.Identity == "column-1"
                ? instance with { Mesh = differentMesh } : instance)),
        };
        renderer.Render(replacement, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
        Require(renderer.RaySceneStatistics!.TopLevelBuilds == builds + 3 && renderer.RaySceneStatistics.MeshBuilds == 3,
            "Changed mesh identity did not rebuild shared acceleration.");
        var before = renderer.RaySceneStatistics;
        renderer.Render(replacement, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
        Require(before == renderer.RaySceneStatistics, "Unchanged snapshot repeated acceleration work.");
    }

    private static TlasTiming MeasureTlas(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box,
        NativeMesh3D sphere, SceneAccelerationUpdateMode3D mode)
    {
        const int width = 1280;
        const int height = 720;
        const int warmup = 16;
        const int measured = 40;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = TlasRenderer(plant, assets, target, mode, LocalLightSamplingMode3D.Importance);
        // Hold the original raster admission behavior fixed for the TLAS A/B control.
        renderer.RasterAdmissionMode = NativeRasterAdmissionMode3D.Rebuild;
        var cpu = new List<double>();
        var gpu = new List<double>();
        var wait = new List<double>();
        NativeInstanceSet3D? previous = null;
        SceneAccelerationStatistics3D? startStatistics = null;
        long started = 0;
        for (int frame = 0; frame < warmup + measured; frame++)
        {
            var scene = TlasMotionScene(box, sphere, previous, frame);
            if (frame == warmup)
            {
                renderer.WaitForPendingFrames();
                startStatistics = renderer.RaySceneStatistics;
                started = Stopwatch.GetTimestamp();
            }
            var result = renderer.Render(scene, Matrix(width, height), Camera.Eye, Clear);
            previous = (NativeInstanceSet3D)scene.Instances;
            if (frame < warmup)
            {
                continue;
            }
            cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds);
            wait.Add(result.CpuTimings.SlotWaitMilliseconds);
            if (result.CompletedGpuFrame is { } completed)
            {
                gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000);
            }
        }
        renderer.WaitForPendingFrames();
        double fps = measured / Stopwatch.GetElapsedTime(started).TotalSeconds;
        var end = renderer.RaySceneStatistics!;
        int updates = end.TopLevelUpdates - startStatistics!.TopLevelUpdates;
        int builds = end.TopLevelBuilds - startStatistics.TopLevelBuilds;
        int allocations = end.SnapshotAllocations - startStatistics.SnapshotAllocations;
        int submits = end.SynchronousBuildSubmissions - startStatistics.SynchronousBuildSubmissions;
        if (mode == SceneAccelerationUpdateMode3D.Automatic)
        {
            Require(updates == measured && builds == 0 && allocations == 0 && submits == 0,
                "Measured motion updated through an allocating or synchronous acceleration path.");
        }
        double cpuMedian = Median(cpu);
        double gpuMedian = Median(gpu);
        Console.WriteLine($"TLAS {mode} 720p: CPU {cpuMedian:F3} ms, GPU {gpuMedian:F3} ms, {fps:F1} fps; builds {builds}, updates {updates}, allocations {allocations}, submits {submits}");
        return new(mode.ToString(), width, height, measured, cpuMedian, Median(wait), gpuMedian, fps,
            builds, updates, allocations, submits);
    }
}
