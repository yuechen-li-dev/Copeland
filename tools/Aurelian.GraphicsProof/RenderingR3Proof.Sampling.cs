using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static readonly LocalLightSamplingMode3D[] SampleModes = [LocalLightSamplingMode3D.Uniform,
        LocalLightSamplingMode3D.Importance, LocalLightSamplingMode3D.Hybrid,
        LocalLightSamplingMode3D.Reservoir, LocalLightSamplingMode3D.TemporalReservoir,
        LocalLightSamplingMode3D.UniformEligible];

    public static void RunSampling(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Fixed-budget local light sampling"));
        Require(initialized.Success, "Sampling plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("sampling-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var rows = new List<object>();
        var pairs = new List<object>();
        var quality = new Dictionary<string, double>();
        foreach (string workload in new[] { "hall", "key-fill" })
        {
            var lights = Lights();
            if (workload == "key-fill")
            {
                lights[0] = new(new(-.8f, 2.4f, .4f), new(1, .6f, .3f), 35, 7) { CastShadows = true };
                lights[1] = new(new(1, 2.3f, .6f), new(.4f, .7f, 1), 25, 7) { CastShadows = true };
            }
            Native3DScene scene = Scene(box, sphere, 0);
            ReferenceImage3D reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, lights);
            string referenceFile = SaveSamplingImage(plant, assets, output, workload + "-reference", reference);
            using var target = new VulkanNativeFrameTarget(plant, Width, Height);
            using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
            renderer.FrameMode = Native3DFrameMode.Interactive;
            renderer.EnableVisibilityBuffer = true;
            renderer.EnableRayShadows = true;
            renderer.Settings = Settings();
            renderer.LocalLights = lights;
            renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
            ReferenceImage3D exact = renderer.ReadLightingForQualification();
            double exactError = Rmse(exact.LinearRgb, reference.LinearRgb);
            Require(exactError < .002, "Exact native HDR differs from reference: " + exactError);
            SaveSamplingImage(plant, assets, output, workload + "-exact", exact);
            foreach (var variant in SampleModes.Select(mode => (Mode: mode, History: 256, Name: mode.ToString()))
                .Append((Mode: LocalLightSamplingMode3D.TemporalReservoir, History: 32, Name: "TemporalReservoir32")))
            {
                var mode = variant.Mode;
                string name = variant.Name;
                renderer.LocalLightSampling = new() { Mode = mode, FrameRayBudget = Width * Height * 4, ExactLights = 2, HistoryLimit = variant.History };
                renderer.ResetLightSamplingHistory();
                float[] sum = new float[Width * Height * 3];
                var singleErrors = new List<double>();
                float[]? previous = null;
                double residual = 0;
                double shortError = 0;
                long lastRays = 0;
                long lastReused = 0;
                for (int frame = 0; frame < 128; frame++)
                {
                    var result = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
                    ReferenceImage3D image = renderer.ReadLightingForQualification();
                    singleErrors.Add(Rmse(image.LinearRgb, reference.LinearRgb));
                    if (previous is not null) residual += Rmse(image.LinearRgb, previous);
                    previous = image.LinearRgb;
                    for (int index = 0; index < sum.Length; index++) sum[index] += image.LinearRgb[index];
                    if (frame == 15 || frame == 127)
                    {
                        var mean = new ReferenceImage3D(Width, Height, frame + 1, sum.Select(value => value / (frame + 1)).ToArray());
                        double error = Rmse(mean.LinearRgb, reference.LinearRgb);
                        string file = SaveSamplingImage(plant, assets, output, $"{workload}-{name}-mean-{frame + 1}", mean);
                        if (frame == 15) shortError = error;
                        else
                        {
                            quality[workload + "-" + name] = error;
                            pairs.Add(new { Name = workload + "-" + name, Reference = referenceFile, Test = file, Group = workload + "-" + name });
                            NativeGameGraphics.WritePng(Path.Combine(output, $"{workload}-{name}-single.png"), Width, Height, result.Pixels!);
                        }
                        float[] samples = renderer.ReadLightSamplesForQualification();
                        lastRays = SumLane(samples, 8);
                        lastReused = SumLane(samples, 10);
                        Require(SumLane(samples, 9) == Width * Height * 4 && lastRays <= Width * Height * 4,
                            "GPU work exceeded the declared frame ray budget.");
                    }
                }
                double meanError = quality[workload + "-" + name];
                Require(double.IsFinite(meanError), $"Sampled estimator produced nonfinite reference error: {workload} {mode} {meanError}.");
                if (mode != LocalLightSamplingMode3D.TemporalReservoir)
                    Require(meanError < shortError * .8, "Independent-frame mean did not converge: " + mode);
                if (mode == LocalLightSamplingMode3D.TemporalReservoir) Require(lastReused > 0, "Temporal reservoirs were never reused.");
                Console.WriteLine($"Sampling {workload} {name}: mean16 {shortError:F6}, mean128 {meanError:F6}, single {singleErrors.Average():F6}, temporal delta {residual / 127:F6}, rays {lastRays}, reuse {lastReused}");
                rows.Add(new { Workload = workload, Mode = name, HistoryLimit = variant.History, RayBudget = Width * Height * 4,
                    ActualLastFrameRays = lastRays, ReusedReservoirs = lastReused, Mean16LinearRmse = shortError,
                    Mean128LinearRmse = meanError, MeanSingleLinearRmse = singleErrors.Average(),
                    TemporalDeltaLinearRmse = residual / 127, ExactLinearRmse = exactError });
                // Explicit reset must restore the first sample, including an old populated history allocation.
                renderer.ResetLightSamplingHistory();
                byte[] first = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
                renderer.ResetLightSamplingHistory();
                byte[] reset = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
                Require(first.SequenceEqual(reset), "Sampling reset retained stale random or reservoir state.");
            }
            renderer.LocalLightSampling = new() { Mode = LocalLightSamplingMode3D.Uniform, FrameRayBudget = Width * Height + Width * Height / 3 };
            renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
            var uneven = renderer.ReadLightSamplesForQualification();
            Require(SumLane(uneven, 9) == renderer.LocalLightSampling.FrameRayBudget
                && SumLane(uneven, 8) <= renderer.LocalLightSampling.FrameRayBudget, "Remainder quota allocation violated the global ray cap.");
            Require(quality[workload + "-Importance"] < quality[workload + "-UniformEligible"] * .9,
                "Weighted proposal failed to improve uniform selection among contributing candidates.");
            Require(quality[workload + "-Importance"] < quality[workload + "-Uniform"] * .8,
                "Importance sampling failed to improve the uniform control.");
        }
        object motion = CheckSamplingMotion(plant, assets, box, sphere);
        object taa = CheckSampledTaa(plant, assets, box, sphere, output);
        var timings = new List<SamplingTiming>();
        foreach (var mode in new[] { LocalLightSamplingMode3D.Exact }.Concat(SampleModes))
            timings.Add(MeasureSampling(plant, assets, box, sphere, mode));
        timings.Add(MeasureSampling(plant, assets, box, sphere, LocalLightSamplingMode3D.TemporalReservoir, historyLimit: 32));
        Require(timings.Single(row => row.Mode == "Importance").MedianGpuMilliseconds
            < timings.Single(row => row.Mode == "Exact").MedianGpuMilliseconds * .95,
            "Importance sampling failed to reduce total GPU frame cost by at least five percent.");
        pairs.Add(new { Name = "identity", Reference = "hall-reference.png", Test = "hall-reference.png", Group = "identity" });
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "local-light-sampling", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, new JsonSerializerOptions { WriteIndented = true }));
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, FullR3Complete = false, FullRestirDi = false, Device = plant.Facts.PhysicalDeviceName,
            Width, Height, FramesPerMean = 128, Quality = rows, Motion = motion, TemporalAa = taa, Measurements = timings,
            Limits = "Point/spot opaque snapshots. Unshadowed proposal. Same-pixel temporal RIS with receiver validation; no spatial reuse, motion reprojection or denoising. Exact remains default.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_LIGHT_SAMPLING_PASSED " + output);
    }

    private static object CheckSamplingMotion(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere)
    {
        const int size = 48;
        var camera = new ReferenceCamera3D(new(0, .01f, 0), new(0, -1, 0), -Vector3.UnitZ, 1, 2);
        var projection = Matrix4x4.CreateOrthographic(2, 2, .001f, 100);
        projection.M22 *= -1;
        var material = new ModelMaterial("sampling-floor") { BaseColor = new(.7f, .5f, .3f, 1), Roughness = .65f, Metallic = 0, DoubleSided = true };
        var floor = new SceneInstance3D("floor", box, material, Matrix4x4.CreateScale(2, .01f, 2) * Matrix4x4.CreateTranslation(0, -.01f, 0));
        using var target = new VulkanNativeFrameTarget(plant, size, size);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.EnableRayShadows = true;
        renderer.Settings = Settings();
        renderer.LocalLights = [new(new(0, 2, 0), Vector3.One, 5, 5) { CastShadows = true }];
        using var exactTarget = new VulkanNativeFrameTarget(plant, size, size);
        using var exactRenderer = RenderingBindlessProof.CreateRenderer(plant, assets, exactTarget, enableCulling: true, rayShadows: true);
        exactRenderer.EnableVisibilityBuffer = true;
        exactRenderer.EnableRayShadows = true;
        exactRenderer.Settings = Settings();
        exactRenderer.LocalLights = renderer.LocalLights;
        renderer.LocalLightSampling = new() { Mode = LocalLightSamplingMode3D.TemporalReservoir, FrameRayBudget = size * size * 4 };
        var observations = new List<object>();
        byte[]? prior = null;
        for (int pose = 0; pose < 3; pose++)
        {
            var blocker = new SceneInstance3D("moving", sphere, material,
                Matrix4x4.CreateScale(.28f, .4f, .28f) * Matrix4x4.CreateTranslation(pose * .7f, 1, 0));
            var scene = new Native3DScene([], []) { Instances = new NativeInstanceSet3D([floor, blocker]) };
            var matrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
            byte[] exact = exactRenderer.Render(scene, matrix, camera.Eye, Clear, capture: true).Pixels!;
            if (pose == 0)
                for (int warmup = 0; warmup < 5; warmup++) renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            byte[] sampled = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true).Pixels!;
            double error = Error(exact, sampled);
            Require(error < .00001, "Single-source temporal RIS retained stale shadow visibility.");
            if (prior is not null) Require(!sampled.SequenceEqual(prior), "Moving occluder shadow was frozen.");
            prior = sampled;
            long reused = SumLane(renderer.ReadLightSamplesForQualification(), 10);
            Require(reused > 0, "Motion control never exercised reused reservoirs.");
            observations.Add(new { Pose = pose, DisplayRgbMae = error, Reused = reused });
            if (pose == 2)
            {
                // A camera move changes receivers; no motion reprojection is being claimed.
                var shifted = Matrix4x4.CreateLookAt(camera.Eye + new Vector3(.25f, 0, 0), camera.Target + new Vector3(.25f, 0, 0), camera.Up) * projection;
                renderer.Render(scene, shifted, camera.Eye + new Vector3(.25f, 0, 0), Clear, capture: true);
                Require(SumLane(renderer.ReadLightSamplesForQualification(), 10) == 0, "Changed receiver accepted stale same-pixel history.");
            }
        }
        // A hybrid with fewer lights than exact slots has an empty tail, not an invalid sample.
        var one = new Native3DScene([], []) { Instances = new NativeInstanceSet3D([floor]) };
        var oneMatrix = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
        byte[] oneExact = exactRenderer.Render(one, oneMatrix, camera.Eye, Clear, capture: true).Pixels!;
        foreach (var mode in SampleModes)
        {
            renderer.LocalLightSampling = new() { Mode = mode, FrameRayBudget = size * size * 4, ExactLights = 3 };
            byte[] actual = renderer.Render(one, oneMatrix, camera.Eye, Clear, capture: true).Pixels!;
            Require(Error(actual, oneExact) < .00001, "Single-light or empty hybrid tail changed radiance: " + mode);
        }
        return new { Poses = observations, ChangedReceiversRejected = true, EmptyHybridTailQualified = true, VisibilityRetraced = true };
    }

    private static object CheckSampledTaa(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere, string output)
    {
        Native3DScene scene = Scene(box, sphere, 0);
        byte[]? exact = null;
        var errors = new Dictionary<string, double>();
        foreach (var mode in new[] { LocalLightSamplingMode3D.Exact, LocalLightSamplingMode3D.Uniform,
            LocalLightSamplingMode3D.Importance, LocalLightSamplingMode3D.Hybrid, LocalLightSamplingMode3D.TemporalReservoir })
        {
            using var target = new VulkanNativeFrameTarget(plant, Width, Height);
            using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
            renderer.EnableVisibilityBuffer = true;
            renderer.EnableRayShadows = true;
            renderer.Settings = Settings() with { AntiAliasing = AntiAliasing3D.Temporal };
            renderer.LocalLights = Lights();
            renderer.LocalLightSampling = new() { Mode = mode, FrameRayBudget = Width * Height * 4, ExactLights = 2 };
            byte[] pixels = [];
            for (int frame = 0; frame < 64; frame++)
                pixels = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
            if (mode == LocalLightSamplingMode3D.Exact) exact = pixels;
            errors[mode.ToString()] = Error(pixels, exact!);
            NativeGameGraphics.WritePng(Path.Combine(output, $"taa-{mode}.png"), Width, Height, pixels);
            Console.WriteLine($"Sampling native TAA {mode}: display MAE vs exact TAA {errors[mode.ToString()]:F6}");
        }
        Require(errors["Importance"] < errors["Uniform"] * .6, "Ordinary native TAA failed to distinguish importance and uniform sampling.");
        return new { Frames = 64, Control = "Native exact-light TAA with the same jitter sequence; not an independent path-traced AA reference", DisplayRgbMae = errors };
    }

    private sealed record SamplingTiming(string Mode, int Width, int Height, int RayBudget,
        double MedianGpuMilliseconds, double MedianCpuPrepareSubmitMilliseconds, double FramesPerSecond);

    private static SamplingTiming MeasureSampling(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere, LocalLightSamplingMode3D mode, int historyLimit = 256)
    {
        const int width = 1280;
        const int height = 720;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.EnableRayShadows = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Settings();
        renderer.LocalLights = Lights();
        renderer.LocalLightSampling = new() { Mode = mode, FrameRayBudget = width * height * 4, ExactLights = 2, HistoryLimit = historyLimit };
        var scene = Scene(box, sphere, 0);
        var gpu = new List<double>();
        var cpu = new List<double>();
        long started = 0;
        for (int frame = 0; frame < 64; frame++)
        {
            if (frame == 24) started = Stopwatch.GetTimestamp();
            var result = renderer.Render(scene, Matrix(width, height), Camera.Eye, Clear);
            if (frame < 24) continue;
            cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds);
            if (result.CompletedGpuFrame is { } complete) gpu.Add((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);
            Require(result.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0 }, "Sampled frame rebuilt scene admission.");
        }
        renderer.WaitForPendingFrames();
        double fps = 40 / Stopwatch.GetElapsedTime(started).TotalSeconds;
        double median = Median(gpu);
        string name = historyLimit == 32 ? "TemporalReservoir32" : mode.ToString();
        Console.WriteLine($"Sampling performance {name}: GPU {median:F3} ms, CPU {Median(cpu):F3} ms, {fps:F1} fps");
        return new(name, width, height, width * height * 4, median, Median(cpu), fps);
    }

    private static long SumLane(float[] data, int lane)
    {
        long sum = 0;
        for (int index = lane; index < data.Length; index += 16) sum += (long)data[index];
        return sum;
    }
    private static double Rmse(float[] first, float[] second)
    {
        double sum = 0;
        for (int index = 0; index < first.Length; index++) sum += Math.Pow(first[index] - second[index], 2);
        return Math.Sqrt(sum / first.Length);
    }
    private static string SaveSamplingImage(AurelianVulkanPlant plant, GameAssets assets, string output, string name, ReferenceImage3D image)
    {
        RenderingReferenceProof.WriteImage(output, name, image);
        string file = name + ".png";
        NativeGameGraphics.WritePng(Path.Combine(output, file), image.Width, image.Height,
            VulkanReferenceOutput3D.ToneMap(plant, assets.Shader("ToneMap3D.v.ts"), image));
        return file;
    }
}
