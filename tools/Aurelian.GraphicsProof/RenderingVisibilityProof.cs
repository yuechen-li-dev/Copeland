using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingVisibilityProof
{
    private const int Width = 640;
    private const int Height = 360;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian visibility-buffer research"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        Require(plant.Facts.VisibilityBufferEnabled, "Device does not admit typed visibility/primitive identity.");
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target);
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        var comparisons = new List<object>();
        var measurements = new List<Measurement>();
        var tree = RenderingR2Proof.Tree();
        Native3DScene lastForest = null!;
        foreach (int materialCount in new[] { 1, 64, 512 })
        {
            ModelMaterial[] materials = RenderingBindlessProof.Materials(materialCount);
            var instances = Enumerable.Range(0, 20_000).Select(index => new SceneInstance3D("tree-" + index, tree,
                materials[index % materialCount], Matrix4x4.CreateScale(.8f + index % 7 * .07f)
                    * Matrix4x4.CreateRotationY(index * .71f)
                    * Matrix4x4.CreateTranslation((index % 200 - 100) * 2.5f, 0, -(index / 200) * 2.5f)));
            Native3DScene scene = new([], []) { Instances = new NativeInstanceSet3D(instances) };
            lastForest = scene;
            for (int pose = 0; pose < 4; pose++)
            {
                Vector3 eye = new(pose * 2, 2.5f + pose * .2f, 8 - pose);
                var camera = RenderingBindlessProof.Camera(eye, new(0, 1.4f, -20));
                comparisons.Add(Compare(renderer, scene, camera, eye, output, $"forest-{materialCount}-{pose}"));
            }
            Vector3 benchmarkEye = new(0, 3, 8);
            Measure(renderer, scene, RenderingBindlessProof.Camera(benchmarkEye, new(0, 1.4f, -20)), benchmarkEye,
                "forest-" + materialCount, measurements);
        }
        ModelMaterial opaque = RenderingBindlessProof.Materials(1)[0] with { Slot = "overdraw", AlphaMask = false };
        var quad = Quad();
        Native3DScene layers = new([], [])
        {
            Instances = new NativeInstanceSet3D(Enumerable.Range(0, 128).Select(index => new SceneInstance3D("layer-" + index,
                quad, opaque, Matrix4x4.CreateScale(4) * Matrix4x4.CreateTranslation(0, 0, -1 + index * .005f)))),
        };
        Vector3 layerEye = new(0, 0, 5);
        Matrix4x4 layerCamera = RenderingBindlessProof.Camera(layerEye, Vector3.Zero);
        comparisons.Add(Compare(renderer, layers, layerCamera, layerEye, output, "overdraw-128"));
        Measure(renderer, layers, layerCamera, layerEye, "overdraw-128", measurements);
        var boundaries = CheckBoundaries(plant, assets, output, lastForest, measurements);
        CheckPerformance(measurements, "overdraw-128", .8);
        CheckPerformance(measurements, "forest-512", .95);
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts, Comparisons = comparisons, Measurements = measurements,
            WarmupFrames = 128, MeasuredFrames = 96,
            QualityBudget = new { MeanAbsoluteByteError = .25, RootMeanSquaredByteError = 1.0, FractionOfRgbChannelsAboveThree = .001 },
            Resolve = "Fullscreen ID-driven reconstruction with explicit texture gradients and shared material evaluation",
            Boundaries = boundaries,
            RuntimeTieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_VISIBILITY_PASSED " + output);
    }

    private static object Compare(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera,
        Vector3 eye, string output, string name)
    {
        var clear = new NativeFrameClearColor(.1f, .15f, .22f, 1);
        renderer.EnableVisibilityBuffer = false;
        renderer.ResetTemporalHistory();
        var reference = renderer.Render(scene, camera, eye, clear, capture: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.ResetTemporalHistory();
        var actual = renderer.Render(scene, camera, eye, clear, capture: true);
        Require(actual.Retained?.VisibilityBuffer == true, "Requested visibility path was not executed.");
        var metrics = ComparePixels(reference.Pixels!, actual.Pixels!);
        Require(metrics.MeanAbsoluteError <= .25 && metrics.RootMeanSquaredError <= 1 && metrics.FractionAboveThree <= .001,
            name + ": reconstructed shading exceeds the frozen image-error budget.");
        NativeGameGraphics.WritePng(Path.Combine(output, name + "-raster.png"), Width, Height, reference.Pixels!);
        NativeGameGraphics.WritePng(Path.Combine(output, name + "-visibility.png"), Width, Height, actual.Pixels!);
        Console.WriteLine($"{name}: mean {metrics.MeanAbsoluteError:F4}, max {metrics.MaximumError}, >3 {metrics.FractionAboveThree:P3}");
        File.WriteAllText(Path.Combine(output, name + ".frame-plan.json"), renderer.CurrentFramePlan!.Describe());
        string coverage = CheckCoverage(renderer, scene, camera, eye, clear);
        return new { Name = name, Reference = reference.PixelSha256, Actual = actual.PixelSha256,
            Metrics = metrics, CoverageSha256 = coverage, actual.Retained };
    }

    private static string CheckCoverage(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera,
        Vector3 eye, NativeFrameClearColor clear)
    {
        var settings = renderer.Settings;
        renderer.Settings = settings with
        {
            SurfaceDebugView = SurfaceDebugView3D.AmbientOcclusion, AmbientOcclusionStrength = 0,
            AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0,
        };
        renderer.EnableVisibilityBuffer = false;
        renderer.ResetTemporalHistory();
        var reference = renderer.Render(scene, camera, eye, clear, capture: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.ResetTemporalHistory();
        var actual = renderer.Render(scene, camera, eye, clear, capture: true);
        renderer.Settings = settings;
        Require(reference.PixelSha256 == actual.PixelSha256, "White-foreground coverage comparison differs, including MASK holes or background.");
        return actual.PixelSha256!;
    }

    private static void Measure(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera,
        Vector3 eye, string name, List<Measurement> measurements)
    {
        var clear = new NativeFrameClearColor(.1f, .15f, .22f, 1);
        const int warmupFrames = 128;
        const int measuredFrames = 96;
        foreach (bool visibility in new[] { false, true, true, false })
        {
            renderer.EnableVisibilityBuffer = visibility;
            renderer.Render(scene, camera, eye, clear, capture: true);
            var cpu = new List<double>();
            var gpu = new List<double>();
            var submit = new List<double>();
            var passes = new Dictionary<string, List<double>>();
            Native3DFrameResult result = null!;
            long started = 0;
            for (int frame = 0; frame < warmupFrames + measuredFrames; frame++)
            {
                if (frame == warmupFrames) started = Stopwatch.GetTimestamp();
                result = renderer.Render(scene, camera, eye, clear);
                if (frame < warmupFrames) continue;
                cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                submit.Add(result.CpuTimings.SubmitMilliseconds);
                if (result.CompletedGpuFrame is { } completed)
                    gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d);
                foreach (var pass in result.GpuPassTimes)
                {
                    if (!passes.TryGetValue(pass.Pass, out var samples)) passes.Add(pass.Pass, samples = []);
                    samples.Add(pass.Milliseconds);
                }
                Require(result.Retained is { InstanceUploadBytes: 0, CpuVisitedInstances: 0 }, "A steady frame revisited resident instances.");
            }
            renderer.WaitForPendingFrames();
            double fps = measuredFrames / Stopwatch.GetElapsedTime(started).TotalSeconds;
            Require(gpu.Count > 0, "No GPU timestamps were measured.");
            measurements.Add(new(name, visibility, Median(cpu), Median(submit), Median(gpu), fps,
                passes.ToDictionary(pair => pair.Key, pair => Median(pair.Value)), result.Retained!));
            Console.WriteLine($"{name}, visibility={visibility}: CPU {Median(cpu):F3}, GPU {Median(gpu):F3} ms, {fps:F1} FPS");
        }
    }

    private static NativeMesh3D Quad() => new("visibility-quad", [
        new(new(-1, -1, 0), Vector3.UnitZ, Vector4.One, new(0, 0), new(1, 0, 0, 1)),
        new(new(1, -1, 0), Vector3.UnitZ, Vector4.One, new(1, 0), new(1, 0, 0, 1)),
        new(new(1, 1, 0), Vector3.UnitZ, Vector4.One, new(1, 1), new(1, 0, 0, 1)),
        new(new(-1, 1, 0), Vector3.UnitZ, Vector4.One, new(0, 1), new(1, 0, 0, 1)),
    ], [0, 1, 2, 0, 2, 3]);

    private sealed record Measurement(string Name, bool Visibility, double CpuMilliseconds, double SubmitMilliseconds,
        double GpuMilliseconds, double FramesPerSecond, Dictionary<string, double> Passes, Native3DRetainedStatistics Retained);

    private static void CheckPerformance(IReadOnlyList<Measurement> measurements, string name, double ratio)
    {
        var samples = measurements.Where(measurement => measurement.Name == name).ToArray();
        double slowestVisibility = samples.Where(sample => sample.Visibility).Max(sample => sample.GpuMilliseconds);
        double fastestRaster = samples.Where(sample => !sample.Visibility).Min(sample => sample.GpuMilliseconds);
        Require(slowestVisibility < fastestRaster * ratio, name + ": repeated controls do not show the required full-frame GPU improvement.");
    }

    private sealed record PixelMetrics(double MeanAbsoluteError, double RootMeanSquaredError, int MaximumError, double FractionAboveThree);
    private static PixelMetrics ComparePixels(byte[] reference, byte[] actual)
    {
        long sum = 0;
        long squared = 0;
        int maximum = 0;
        int above = 0;
        int channels = 0;
        for (int index = 0; index < reference.Length; index++)
        {
            if (index % 4 == 3) continue;
            int difference = Math.Abs(reference[index] - actual[index]);
            sum += difference;
            squared += difference * difference;
            maximum = Math.Max(maximum, difference);
            if (difference > 3) above++;
            channels++;
        }
        return new((double)sum / channels, Math.Sqrt((double)squared / channels), maximum, (double)above / channels);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
