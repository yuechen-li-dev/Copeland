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
    public static void RunArea(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Area lights and local tail reconstruction"));
        Require(initialized.Success, "Area lighting initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("area-occluder", AnalyticPrimitiveKind3D.UnitSphere);
        var rows = new List<object>();
        var pairs = new List<object>();
        foreach (string workload in new[] { "area-only", "key-tail", "many-area" })
        {
            var scene = Scene(box, sphere, 0);
            var lights = AreaLights(workload);
            var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, lights, samples: 512);
            string referenceFile = SaveSamplingImage(plant, assets, output, workload + "-reference", reference);
            var errors = new Dictionary<string, double>();
            foreach (var variant in new[]
            {
                (Name: "importance", Mode: LocalLightSamplingMode3D.Importance, Filter: false, Passes: 0),
                (Name: "hybrid", Mode: LocalLightSamplingMode3D.Hybrid, Filter: false, Passes: 0),
                (Name: "hybrid-temporal", Mode: LocalLightSamplingMode3D.Hybrid, Filter: true, Passes: 0),
                (Name: "hybrid-tail-1", Mode: LocalLightSamplingMode3D.Hybrid, Filter: true, Passes: 1),
                (Name: "hybrid-tail", Mode: LocalLightSamplingMode3D.Hybrid, Filter: true, Passes: 2),
                (Name: "restir", Mode: LocalLightSamplingMode3D.SpatiotemporalReservoir, Filter: false, Passes: 0),
                (Name: "restir-tail", Mode: LocalLightSamplingMode3D.SpatiotemporalReservoir, Filter: true, Passes: 1),
            })
            {
                using var target = new VulkanNativeFrameTarget(plant, Width, Height);
                using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, variant.Mode);
                renderer.LocalLights = lights;
                renderer.LocalLightSampling = RestirOptions(variant.Mode) with
                {
                    ExactLights = 2,
                    ReconstructTail = variant.Filter,
                    TailHistoryLimit = 8,
                    TailSpatialPasses = variant.Passes,
                };
                double error = 0;
                double delta = 0;
                float[] mean = new float[Width * Height * 3];
                float[]? previous = null;
                for (int frame = 0; frame < 80; frame++)
                {
                    var captured = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
                    var image = renderer.ReadLightingForQualification();
                    var samples = renderer.ReadLightSamplesForQualification();
                    Require(SumLane(samples, 9) == Width * Height * 4 && SumLane(samples, 8) <= Width * Height * 4,
                        "Area lighting exceeded its complete frame ray budget.");
                    if (frame >= 16)
                    {
                        error += Rmse(image.LinearRgb, reference.LinearRgb);
                        if (previous is not null) delta += Rmse(image.LinearRgb, previous);
                        previous = image.LinearRgb;
                        for (int index = 0; index < mean.Length; index++) mean[index] += image.LinearRgb[index] / 64;
                    }
                    if (frame == 79)
                    {
                        string file = workload + "-" + variant.Name + ".png";
                        NativeGameGraphics.WritePng(Path.Combine(output, file), Width, Height, captured.Pixels!);
                        pairs.Add(new { Name = workload + "-" + variant.Name, Reference = referenceFile, Test = file, Group = workload + "-" + variant.Name });
                    }
                }
                double meanError = Rmse(mean, reference.LinearRgb);
                double energy = mean.Sum(value => (double)value) / reference.LinearRgb.Sum(value => (double)value);
                Require(double.IsFinite(meanError) && energy > .90 && energy < 1.10, "Area estimator energy drift: " + energy);
                errors[variant.Name] = error / 64;
                rows.Add(new
                {
                    Workload = workload,
                    Variant = variant.Name,
                    MeanFrameRmse = error / 64,
                    MeanImageRmse = meanError,
                    TemporalDeltaRmse = delta / 63,
                    MeanEnergyRatio = energy
                });
                Console.WriteLine($"Area {workload} {variant.Name}: frame {error / 64:F6}, mean {meanError:F6}, delta {delta / 63:F6}, energy {energy:F6}");
            }
            Require(errors["hybrid-tail-1"] < errors["hybrid"] * .9, "Tail reconstruction did not improve reference error.");
        }
        object normalization = CheckSphereNormalization(plant, assets);
        object head = CheckExactHeadReconstruction(plant, assets, box, sphere);
        object motion = CheckAreaMotion(plant, assets, box, sphere, output);
        object measurements = MeasureArea(plant, assets, box, sphere);
        validation.RequireClean();
        pairs.Add(new { Name = "identity", Reference = "area-only-reference.png", Test = "area-only-reference.png", Group = "identity" });
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        { Qualification = "area-tail", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Quality = rows,
            Normalization = normalization,
            Head = head,
            Motion = motion,
            Measurements = measurements
        },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_AREA_PASSED " + output);
    }

    private static object MeasureArea(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere)
    {
        var rows = new List<object>();
        foreach (var mode in new[] { LocalLightSamplingMode3D.Importance, LocalLightSamplingMode3D.Hybrid })
        {
            foreach (bool filter in new[] { false, true })
            {
                using var target = new VulkanNativeFrameTarget(plant, 1280, 720);
                using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, mode);
                renderer.LocalLights = AreaLights("many-area");
                renderer.LocalLightSampling = RestirOptions(mode, 1280 * 720) with
                { ExactLights = 2, ReconstructTail = filter, TailHistoryLimit = 8, TailSpatialPasses = 1 };
                var scene = Scene(box, sphere, 0);
                var cpu = new List<double>();
                var gpu = new List<double>();
                long started = 0;
                for (int frame = 0; frame < 64; frame++)
                {
                    if (frame == 24)
                    {
                        renderer.WaitForPendingFrames();
                        started = Stopwatch.GetTimestamp();
                    }
                    var result = renderer.Render(scene, Matrix(1280, 720), Camera.Eye, Clear);
                    if (frame >= 24)
                    {
                        cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds);
                        if (result.CompletedGpuFrame is { } complete)
                            gpu.Add((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);
                    }
                }
                renderer.WaitForPendingFrames();
                double fps = 40 / Stopwatch.GetElapsedTime(started).TotalSeconds;
                rows.Add(new
                {
                    Mode = mode.ToString(),
                    ReconstructTail = filter,
                    MedianGpuMilliseconds = Median(gpu),
                    MedianCpuPrepareSubmitMilliseconds = Median(cpu),
                    WallFramesPerSecond = fps
                });
                Console.WriteLine($"Area 720p {mode} tail={filter}: GPU {Median(gpu):F3} ms, CPU {Median(cpu):F3} ms, {fps:F1} fps");
            }
        }
        return rows;
    }

    private static object CheckSphereNormalization(AurelianVulkanPlant plant, GameAssets assets)
    {
        var material = new ReferenceMaterial3D(new(.4f, .5f, .6f), .8f, Dielectric: 0);
        ReferenceTriangle3D[] floor =
        [
            new(new(-2,0,-2), new(-2,0,2), new(2,0,2), material),
            new(new(-2,0,-2), new(2,0,2), new(2,0,-2), material),
        ];
        var light = new LocalLight3D(new(0, 2, 0), new(3, 4, 5), 1, 10)
        { Kind = LocalLightKind.Sphere, EmitterRadius = .5f };
        using var reference = new ReferenceIntegrator3D(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"),
            new(1, 1, 1, 918, PixelJitter: false), new(new(0, 1, 0), Vector3.Zero, Vector3.UnitZ, .6f),
            floor, [], lighting: new(LocalLights: [light]));
        reference.Accumulate(4096);
        float fade = MathF.Pow(1 - MathF.Pow(4f / 100, 2), 2);
        Vector3 expected = material.BaseColor * light.Color * (.25f / 4) * fade;
        float[] actual = reference.Capture().LinearRgb;
        double relative = Rmse(actual, [expected.X, expected.Y, expected.Z]) / expected.Length();
        Require(relative < .005, "Sphere solid-angle estimator disagrees with analytic Lambert irradiance: " + relative);
        Console.WriteLine($"Area sphere analytic normalization relative RMS {relative:F6}");
        return new { Samples = 4096, RelativeRms = relative, Expected = new[] { expected.X, expected.Y, expected.Z }, Actual = actual };
    }

    private static LocalLight3D[] AreaLights(string workload)
    {
        var sphere = new LocalLight3D(new(0, 3, .5f), new(1, .8f, .55f), 12, 12)
        { Kind = LocalLightKind.Sphere, EmitterRadius = .65f, CastShadows = true };
        if (workload == "area-only") return [sphere];
        LocalLight3D[] head =
        [
            new(new(-1.8f, 2.5f, 1), new(1,.6f,.3f), 10, 10) { CastShadows = true },
            new(new(2, 2.8f, -.7f), new(.3f,.6f,1), 8, 10) { CastShadows = true },
        ];
        if (workload == "key-tail") return [.. head, sphere];
        return [.. head, .. Enumerable.Range(0, 16).Select(index => sphere with
        {
            Position = new(-2.4f + index % 4 * 1.6f, 3, -2.4f + index / 4 * 1.6f),
            EmitterRadius = .18f + index % 3 * .08f,
            Color = new(.5f + index % 3 * .2f, .6f, 1),
        })];
    }

    private static object CheckExactHeadReconstruction(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere)
    {
        var scene = Scene(box, sphere, 0);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var controlTarget = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Hybrid);
        using var control = TlasRenderer(plant, assets, controlTarget, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        LocalLight3D[] lights = [new(new(0, 3, 1), Vector3.One, 6, 10) { CastShadows = true }];
        renderer.LocalLights = lights;
        control.LocalLights = lights;
        renderer.LocalLightSampling = RestirOptions(LocalLightSamplingMode3D.Hybrid) with { ReconstructTail = true, ExactLights = 2 };
        for (int frame = 0; frame < 16; frame++)
        {
            byte[] actual = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
            byte[] expected = control.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
            Require(actual.SequenceEqual(expected), "Tail reconstruction changed the exact head.");
        }
        renderer.ResetTemporalHistory();
        byte[] cut = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
        renderer.ResetTemporalHistory();
        byte[] repeat = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
        Require(cut.SequenceEqual(repeat), "Tail camera-cut reset was not deterministic.");
        renderer.LocalLights = AreaLights("key-tail");
        control.LocalLights = renderer.LocalLights.Take(2).ToArray();
        for (int frame = 0; frame < 16; frame++)
        {
            renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
            control.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
            Require(renderer.ReadLocalHeadForQualification().LinearRgb.SequenceEqual(control.ReadLightingForQualification().LinearRgb),
                "A stochastic area tail contaminated the current exact head.");
        }
        renderer.ResetTemporalHistory();
        cut = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
        renderer.ResetTemporalHistory();
        repeat = renderer.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true).Pixels!;
        Require(cut.SequenceEqual(repeat), "Nonzero tail did not reset on a camera cut.");
        control.LocalLights = AreaLights("area-only");
        try
        {
            control.Render(scene, Matrix(Width, Height), Camera.Eye, Clear, capture: true);
            throw new InvalidDataException("An area light entered the exact delta-mask path.");
        }
        catch (NotSupportedException failure)
        {
            Require(failure.Message.Contains("AUR-AREA-001"), failure.Message);
        }
        return new { ExactHeadByteIdenticalFrames = 32, CameraCutReset = true, ExactAreaRejected = true };
    }

    private static object CheckAreaMotion(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere, string output)
    {
        var rows = new List<object>();
        foreach (string sequence in new[] { "occluder", "camera" })
        {
            foreach (bool filter in new[] { false, true })
            {
                using var target = new VulkanNativeFrameTarget(plant, Width, Height);
                using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Hybrid);
                renderer.LocalLights = AreaLights("key-tail");
                renderer.LocalLightSampling = RestirOptions(LocalLightSamplingMode3D.Hybrid) with { ExactLights = 2, ReconstructTail = filter };
                NativeInstanceSet3D? previous = null;
                double error = 0;
                double ghost = 0;
                double lag = 0;
                double changeEnergy = 0;
                float[]? priorReference = null;
                uint[]? priorIds = null;
                var staticScene = Scene(box, sphere, 0);
                for (int frame = 0; frame < 24; frame++)
                {
                    var scene = sequence == "occluder" ? TlasMotionScene(box, sphere, previous, frame) : staticScene;
                    var camera = Camera;
                    if (sequence == "camera")
                    {
                        var translation = new Vector3(.4f * MathF.Sin(frame * .25f), 0, 0);
                        camera = Camera with { Eye = Camera.Eye + translation, Target = Camera.Target + translation };
                    }
                    var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, renderer.LocalLights.ToArray(), camera, samples: 128);
                    renderer.Render(scene, RestirMatrix(camera), camera.Eye, Clear, capture: true);
                    var actual = renderer.ReadLightingForQualification();
                    uint[] ids = renderer.ReadIdentitiesForQualification().Ids;
                    error += Rmse(actual.LinearRgb, reference.LinearRgb);
                    if (priorReference is not null)
                    {
                        for (int index = 0; index < actual.LinearRgb.Length; index++)
                        {
                            if (Math.Abs(reference.LinearRgb[index] - priorReference[index]) > .1f)
                                ghost += Math.Abs(actual.LinearRgb[index] - reference.LinearRgb[index]);
                            if (sequence == "occluder" && ids[(index / 3) * 2] == 1 && priorIds![(index / 3) * 2] == 1)
                            {
                                double change = priorReference[index] - reference.LinearRgb[index];
                                if (Math.Abs(change) > .02)
                                {
                                    lag += (actual.LinearRgb[index] - reference.LinearRgb[index]) * change;
                                    changeEnergy += change * change;
                                }
                            }
                        }
                    }
                    priorReference = reference.LinearRgb;
                    priorIds = ids;
                    previous = (NativeInstanceSet3D)scene.Instances;
                }
                double ghostProjection = Math.Max(lag / Math.Max(changeEnergy, 1e-12), 0);
                Require(ghostProjection < .1, "Tail history retained excessive old shadow energy on stable floor receivers.");
                Console.WriteLine($"Area {sequence} filter={filter}: RMSE {error / 24:F6}, changed-reference absolute residual {ghost:F6}, lag {ghostProjection:F6}");
                rows.Add(new
                {
                    Sequence = sequence,
                    Filter = filter,
                    MeanFrameRmse = error / 24,
                    ChangedReferenceAbsoluteResidual = ghost,
                    PreviousReferenceProjection = ghostProjection
                });
            }
        }
        return rows;
    }
}
