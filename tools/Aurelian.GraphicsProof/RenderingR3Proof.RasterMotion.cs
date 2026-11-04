using System.Diagnostics;
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
    private sealed record RasterMotionTiming(string Mode, double CpuPrepareSubmitMilliseconds,
        double SlotWaitMilliseconds, double GpuFrameMilliseconds, double WallFramesPerSecond,
        int TopologyAdmissions, int MotionReuses);

    public static void RunRasterMotion(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Retained raster motion"));
        Require(initialized.Success, "Raster motion initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("raster-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        int comparisons = 0;
        foreach (bool hzb in new[] { false, true })
        {
            foreach (var mode in new[] { LocalLightSamplingMode3D.Exact, LocalLightSamplingMode3D.SpatiotemporalReservoir })
            {
                using var target = new VulkanNativeFrameTarget(plant, Width, Height);
                using var controlTarget = new VulkanNativeFrameTarget(plant, Width, Height);
                using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, mode);
                using var control = TlasRenderer(plant, assets, controlTarget, SceneAccelerationUpdateMode3D.Automatic, mode);
                renderer.EnableHierarchicalZ = hzb;
                control.EnableHierarchicalZ = hzb;
                control.RasterAdmissionMode = NativeRasterAdmissionMode3D.Rebuild;
                NativeInstanceSet3D? previous = null;
                for (int frame = 0; frame < 24; frame++)
                {
                    var scene = TlasMotionScene(box, sphere, previous, frame);
                    var actual = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
                    var expected = control.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
                    Require(actual.Pixels!.SequenceEqual(expected.Pixels!), "Raster motion differs from full re-admission.");
                    Require(actual.Retained!.TopologyAdmissions == 1 && actual.Retained.MotionReuses == frame,
                        "Continuing motion repeated raster topology admission.");
                    var visible = renderer.ReadVisibilityForQualification();
                    Require(visible.All(group => group.Ranks.Select((rank, index) => (rank & 255) != 0 == group.CpuVisible[index]).All(equal => equal)) || hzb,
                        "Moving cluster bounds disagree with uncensored CPU frustum/cone visibility.");
                    previous = (NativeInstanceSet3D)scene.Instances;
                    comparisons++;
                }
                Console.WriteLine($"Raster {mode}, HZB={hzb}: 24 byte-identical motion frames; one topology admission.");
            }
        }
        var timings = new List<RasterMotionTiming>();
        foreach (var mode in new[] { NativeRasterAdmissionMode3D.Rebuild, NativeRasterAdmissionMode3D.Automatic })
        {
            using var target = new VulkanNativeFrameTarget(plant, 1280, 720);
            using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Importance);
            renderer.RasterAdmissionMode = mode;
            NativeInstanceSet3D? previous = null;
            var cpu = new List<double>();
            var wait = new List<double>();
            var gpu = new List<double>();
            long started = 0;
            Native3DRetainedStatistics? before = null;
            Native3DRetainedStatistics? after = null;
            for (int frame = 0; frame < 56; frame++)
            {
                var scene = TlasMotionScene(box, sphere, previous, frame);
                if (frame == 16)
                {
                    renderer.WaitForPendingFrames();
                    before = after;
                    started = Stopwatch.GetTimestamp();
                }
                var result = renderer.Render(scene, Matrix(1280, 720), Camera.Eye, Clear);
                previous = (NativeInstanceSet3D)scene.Instances;
                after = result.Retained;
                if (frame >= 16)
                {
                    cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds);
                    wait.Add(result.CpuTimings.SlotWaitMilliseconds);
                    if (result.CompletedGpuFrame is { } complete)
                    {
                        gpu.Add((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);
                    }
                }
            }
            renderer.WaitForPendingFrames();
            var row = new RasterMotionTiming(mode.ToString(), Median(cpu), Median(wait), Median(gpu),
                40 / Stopwatch.GetElapsedTime(started).TotalSeconds,
                after!.TopologyAdmissions - before!.TopologyAdmissions, after.MotionReuses - before.MotionReuses);
            timings.Add(row);
            Console.WriteLine("Raster 720p: " + row);
        }
        Require(timings[1].TopologyAdmissions == 0 && timings[1].MotionReuses == 40
            && timings[1].CpuPrepareSubmitMilliseconds < timings[0].CpuPrepareSubmitMilliseconds * .5
            && timings[1].WallFramesPerSecond > timings[0].WallFramesPerSecond * 1.25,
            "Raster resource retention did not improve the real moving-frame path.");
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Comparisons = comparisons,
            Measurements = timings,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RASTER_MOTION_PASSED " + output);
    }
}
