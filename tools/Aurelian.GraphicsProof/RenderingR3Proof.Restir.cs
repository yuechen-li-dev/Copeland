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
    private static readonly LocalLightSamplingMode3D[] RestirModes = [LocalLightSamplingMode3D.Reservoir,
        LocalLightSamplingMode3D.TemporalReservoir, LocalLightSamplingMode3D.ReprojectedReservoir,
        LocalLightSamplingMode3D.SpatialReservoir, LocalLightSamplingMode3D.SpatiotemporalReservoir];

    private static LocalLightSampling3D RestirOptions(LocalLightSamplingMode3D mode, int pixels = Width * Height)
        => new()
        {
            Mode = mode,
            FrameRayBudget = pixels * 4,
            Candidates = 16,
            HistoryLimit = 32,
            SpatialNeighbors = 4,
            SpatialRadius = 2
        };

    public static void RunRestir(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Spatial ReSTIR and motion reprojection"));
        Require(initialized.Success, "ReSTIR plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var sphere = NativeAnalyticPrimitives3D.Create("restir-sphere", AnalyticPrimitiveKind3D.UnitSphere);
        byte[] referenceProgram = assets.ComputeShader("ReferenceIntegrator3D.v.ts");
        var pairs = new List<object>();
        var rows = new List<object>();
        foreach (string sequence in new[] { "static", "camera-pan", "moving-receiver" })
        {
            const int measured = 24;
            var references = new List<ReferenceImage3D>();
            NativeInstanceSet3D? previous = null;
            var scenes = new List<Native3DScene>();
            var cameras = new List<ReferenceCamera3D>();
            var baseScene = Scene(box, sphere, 0);
            for (int frame = 0; frame < measured; frame++)
            {
                var source = baseScene;
                if (sequence == "moving-receiver")
                {
                    var items = source.Instances.Select(instance => instance.Identity == "moving"
                        ? instance with { Transform = instance.Transform * Matrix4x4.CreateTranslation(frame * .18f, 0, 0) }
                        : instance).ToArray();
                    var snapshot = new NativeInstanceSet3D(items, previous);
                    source = source with { Instances = snapshot };
                    previous = snapshot;
                }
                scenes.Add(source);
                var camera = Camera;
                if (sequence == "camera-pan")
                {
                    var translation = new Vector3(MathF.Sin(frame * .4f) * .6f, 0, 0);
                    camera = Camera with { Eye = Camera.Eye + translation, Target = Camera.Target + translation };
                }
                cameras.Add(camera);
                references.Add(Reference(plant, referenceProgram, source, Lights(), cameras[^1]));
            }
            var summary = new Dictionary<LocalLightSamplingMode3D, (double Error, double Delta, long Temporal, long Spatial)>();
            foreach (var mode in RestirModes.Prepend(LocalLightSamplingMode3D.Importance))
            {
                using var target = new VulkanNativeFrameTarget(plant, Width, Height);
                using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
                renderer.EnableVisibilityBuffer = true;
                renderer.EnableRayShadows = true;
                renderer.FrameMode = Native3DFrameMode.Interactive;
                renderer.Settings = Settings();
                renderer.LocalLights = Lights();
                renderer.LocalLightSampling = RestirOptions(mode);
                // Warm the same static initial pose without consuming a one-frame motion snapshot.
                var warmScene = scenes[0];
                for (int frame = 0; frame < 16; frame++)
                {
                    renderer.Render(warmScene, RestirMatrix(Camera), Camera.Eye, Clear, capture: true);
                }
                double error = 0;
                double delta = 0;
                float[]? priorResidual = null;
                long temporal = 0;
                long shifted = 0;
                long spatial = 0;
                long rejected = 0;
                long actualRays = 0;
                long movedReceiverPixels = 0;
                long acceptedMovedReceivers = 0;
                double actualEnergy = 0;
                double referenceEnergy = 0;
                double captureCpu = 0;
                uint[]? priorIds = null;
                long revealed = 0;
                long rejectedRevealed = 0;
                for (int frame = 0; frame < measured; frame++)
                {
                    var camera = cameras[frame];
                    var result = renderer.Render(scenes[frame], RestirMatrix(camera), camera.Eye, Clear, capture: true);
                    ReferenceImage3D actual = renderer.ReadLightingForQualification();
                    actualEnergy += actual.LinearRgb.Sum(value => (double)value);
                    referenceEnergy += references[frame].LinearRgb.Sum(value => (double)value);
                    captureCpu += result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds;
                    error += Rmse(actual.LinearRgb, references[frame].LinearRgb);
                    var residual = actual.LinearRgb.Select((value, index) => value - references[frame].LinearRgb[index]).ToArray();
                    if (priorResidual is not null)
                    {
                        delta += Rmse(residual, priorResidual);
                    }
                    priorResidual = residual;
                    float[] samples = renderer.ReadLightSamplesForQualification();
                    Require(SumLane(samples, 9) == Width * Height * 4 && SumLane(samples, 8) <= Width * Height * 4,
                        "Spatial/temporal reuse exceeded the complete frame ray cap.");
                    temporal += SumLane(samples, 10);
                    shifted += SumLane(samples, 11);
                    rejected += SumLane(samples, 12);
                    spatial += SumLane(samples, 13);
                    actualRays += SumLane(samples, 8);
                    if (sequence == "moving-receiver")
                    {
                        uint[] ids = renderer.ReadIdentitiesForQualification().Ids;
                        float[] motion = renderer.ReadLightMotionForQualification();
                        for (int pixel = 0; pixel < Width * Height; pixel++)
                        {
                            if (ids[pixel * 2] != 9)
                            {
                                continue;
                            }
                            float dx = motion[pixel * 4] - ((pixel % Width) + .5f) / Width;
                            float dy = motion[pixel * 4 + 1] - ((pixel / Width) + .5f) / Height;
                            if (Math.Abs(dx) + Math.Abs(dy) > .0001f)
                            {
                                movedReceiverPixels++;
                                if (samples[pixel * 16 + 10] > 0)
                                {
                                    acceptedMovedReceivers++;
                                }
                            }
                        }
                        if (priorIds is not null)
                        {
                            for (int y = 1; y < Height - 1; y++)
                            {
                                for (int x = 1; x < Width - 1; x++)
                                {
                                    int pixel = y * Width + x;
                                    if (ids[pixel * 2] != 1)
                                    {
                                        continue;
                                    }
                                    bool interior = true;
                                    for (int dy = -1; dy <= 1; dy++)
                                    {
                                        for (int dx = -1; dx <= 1; dx++)
                                        {
                                            if (priorIds[((y + dy) * Width + x + dx) * 2] != 9)
                                            {
                                                interior = false;
                                            }
                                        }
                                    }
                                    if (interior)
                                    {
                                        revealed++;
                                        if (samples[pixel * 16 + 12] > .5f)
                                        {
                                            rejectedRevealed++;
                                        }
                                    }
                                }
                            }
                        }
                        priorIds = ids;
                    }
                    if (frame == measured - 1)
                    {
                        string reference = SaveSamplingImage(plant, assets, output, sequence + "-reference", references[frame]);
                        string file = sequence + "-" + mode + ".png";
                        NativeGameGraphics.WritePng(Path.Combine(output, file), Width, Height, result.Pixels!);
                        pairs.Add(new { Name = sequence + "-" + mode, Reference = reference, Test = file, Group = sequence + "-" + mode });
                    }
                }
                double energyRatio = actualEnergy / referenceEnergy;
                Require(energyRatio > .95 && energyRatio < 1.05, "Resampling introduced significant mean-energy drift: " + energyRatio);
                if (sequence == "moving-receiver" && (mode == LocalLightSamplingMode3D.ReprojectedReservoir || mode == LocalLightSamplingMode3D.SpatiotemporalReservoir))
                {
                    Require(acceptedMovedReceivers > 0 && rejectedRevealed == revealed && revealed > 0,
                        "Moving receiver reprojection or interior disocclusion rejection was not exercised.");
                }
                double mean = error / measured;
                double flicker = delta / (measured - 1);
                summary[mode] = (mean, flicker, temporal, spatial);
                if (mode == LocalLightSamplingMode3D.ReprojectedReservoir || mode == LocalLightSamplingMode3D.SpatiotemporalReservoir)
                {
                    Require(temporal > 0 && (sequence == "static" || shifted > 0), "Motion reprojection never accepted shifted receivers.");
                }
                if (mode == LocalLightSamplingMode3D.SpatialReservoir || mode == LocalLightSamplingMode3D.SpatiotemporalReservoir)
                {
                    Require(spatial > 0, "Spatial pass did not merge any neighbor reservoir.");
                }
                Console.WriteLine($"ReSTIR {sequence} {mode}: error {mean:F6}, residual delta {flicker:F6}, temporal {temporal}, shifted {shifted}, spatial {spatial}, rejected {rejected}, revealed {revealed}/{rejectedRevealed}");
                rows.Add(new
                {
                    Sequence = sequence,
                    Mode = mode.ToString(),
                    Frames = measured,
                    MeanLinearRmse = mean,
                    MeanEnergyRatio = energyRatio,
                    MeanCapturePrepareSubmitMilliseconds = captureCpu / measured,
                    MovedReceiverPixels = movedReceiverPixels,
                    AcceptedMovingReceivers = acceptedMovedReceivers,
                    TemporalResidualDeltaRmse = flicker,
                    TemporalAccepted = temporal,
                    ShiftedAccepted = shifted,
                    SpatialAccepted = spatial,
                    TemporalRejected = rejected,
                    RevealedInterior = revealed,
                    RevealedRejected = rejectedRevealed,
                    ActualQueries = actualRays,
                    RayCapPerFrame = Width * Height * 4
                });
            }
            Require(summary[LocalLightSamplingMode3D.SpatialReservoir].Error < summary[LocalLightSamplingMode3D.Reservoir].Error * .9,
                "Spatial reuse failed to improve fresh RIS at the same visibility budget.");
            Require(summary[LocalLightSamplingMode3D.SpatiotemporalReservoir].Error < summary[LocalLightSamplingMode3D.ReprojectedReservoir].Error * .95,
                "Spatial reuse failed to improve motion-reprojected temporal sampling.");
            Require(summary[LocalLightSamplingMode3D.SpatiotemporalReservoir].Error
                < summary[LocalLightSamplingMode3D.Reservoir].Error * .85,
                "Spatiotemporal reuse did not improve current reference error over fresh RIS.");
        }
        object ablations = CheckRestirAblations(plant, assets, box, sphere);
        object taa = CheckRestirJitter(plant, assets, box, sphere, output);
        foreach (string name in new[]
        {
            "Importance", "TemporalReservoir", "ReprojectedReservoir",
            "SpatiotemporalReservoir", "ReprojectedCdf", "SpatiotemporalCdf",
        })
        {
            pairs.Add(new { Name = "taa-" + name, Reference = "taa-Exact.png", Test = "taa-" + name + ".png", Group = "taa-" + name });
        }
        object correctness = new[]
        {
            CheckRestirCorrectness(plant, assets, box, sphere, importanceProposals: false),
            CheckRestirCorrectness(plant, assets, box, sphere, importanceProposals: true),
        };
        var timings = new List<SamplingTiming>();
        foreach (var mode in new[] { LocalLightSamplingMode3D.Exact, LocalLightSamplingMode3D.Importance }.Concat(RestirModes))
        {
            timings.Add(MeasureSampling(plant, assets, box, sphere, mode, historyLimit: 32));
        }
        foreach (var mode in new[] { LocalLightSamplingMode3D.ReprojectedReservoir, LocalLightSamplingMode3D.SpatiotemporalReservoir })
        {
            timings.Add(MeasureSampling(plant, assets, box, sphere, mode, historyLimit: 32, importanceProposals: true));
        }
        validation.RequireClean();
        pairs.Add(new { Name = "identity", Reference = "static-reference.png", Test = "static-reference.png", Group = "identity" });
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        { Qualification = "restir", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            FullR3Complete = false,
            Device = plant.Facts.PhysicalDeviceName,
            Quality = rows,
            Correctness = correctness,
            Ablations = ablations,
            Jitter = taa,
            Measurements = timings,
            Scope = "Opaque point/spot DI: full-support proposal, motion-reprojected temporal resampling, immutable current-frame spatial reuse and fresh final visibility. No area lights or GI."
        },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RESTIR_PASSED " + output);
    }

    private static Matrix4x4 RestirMatrix(ReferenceCamera3D camera)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)Width / Height, .1f, 100);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
    }

    private static object CheckRestirAblations(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere)
    {
        var scene = Scene(box, sphere, 0);
        var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene, Lights());
        var rows = new List<object>();
        foreach (var variant in new[]
        {
            (Mis: false, Importance: false, Radius: 2),
            (Mis: false, Importance: false, Radius: 8),
            (Mis: true, Importance: false, Radius: 2),
            (Mis: true, Importance: false, Radius: 8),
            (Mis: true, Importance: true, Radius: 2),
            (Mis: true, Importance: true, Radius: 8),
        })
        {
            using var target = new VulkanNativeFrameTarget(plant, Width, Height);
            using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
            renderer.EnableVisibilityBuffer = true;
            renderer.EnableRayShadows = true;
            renderer.Settings = Settings();
            renderer.LocalLights = Lights();
            renderer.LocalLightSampling = RestirOptions(LocalLightSamplingMode3D.SpatiotemporalReservoir) with
            {
                SpatialTargetMis = variant.Mis,
                SpatialRadius = variant.Radius,
                ImportanceProposals = variant.Importance,
            };
            double error = 0;
            double delta = 0;
            float[]? previous = null;
            for (int frame = 0; frame < 48; frame++)
            {
                renderer.Render(scene, RestirMatrix(Camera), Camera.Eye, Clear, capture: true);
                if (frame >= 16)
                {
                    float[] actual = renderer.ReadLightingForQualification().LinearRgb;
                    error += Rmse(actual, reference.LinearRgb);
                    if (previous is not null)
                    {
                        delta += Rmse(actual, previous);
                    }
                    previous = actual;
                }
            }
            Console.WriteLine($"ReSTIR ablation MIS={variant.Mis}, radius={variant.Radius}, CDF={variant.Importance}: mean error {error / 32:F6}, delta {delta / 31:F6}");
            rows.Add(new
            {
                PairwiseTargetMis = variant.Mis,
                RadiusPixels = variant.Radius,
                ImportanceProposals = variant.Importance,
                Frames = 32,
                MeanLinearRmse = error / 32,
                TemporalDeltaRmse = delta / 31,
            });
        }
        return rows;
    }

    private static object CheckRestirJitter(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere, string output)
    {
        var scene = Scene(box, sphere, 0);
        var results = new List<object>();
        byte[]? control = null;
        var errors = new Dictionary<string, double>();
        (LocalLightSamplingMode3D Mode, bool Importance, string Name)[] variants =
        {
            (Mode: LocalLightSamplingMode3D.Exact, Importance: false, Name: "Exact"),
            (LocalLightSamplingMode3D.Importance, false, "Importance"),
            (LocalLightSamplingMode3D.TemporalReservoir, false, "TemporalReservoir"),
            (LocalLightSamplingMode3D.ReprojectedReservoir, false, "ReprojectedReservoir"),
            (LocalLightSamplingMode3D.SpatiotemporalReservoir, false, "SpatiotemporalReservoir"),
            (LocalLightSamplingMode3D.ReprojectedReservoir, true, "ReprojectedCdf"),
            (LocalLightSamplingMode3D.SpatiotemporalReservoir, true, "SpatiotemporalCdf"),
        };
        foreach (var variant in variants)
        {
            var mode = variant.Mode;
            using var target = new VulkanNativeFrameTarget(plant, Width, Height);
            using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
            renderer.EnableVisibilityBuffer = true;
            renderer.EnableRayShadows = true;
            renderer.Settings = Settings() with { AntiAliasing = AntiAliasing3D.Temporal };
            renderer.LocalLights = Lights();
            renderer.LocalLightSampling = RestirOptions(mode) with { ImportanceProposals = variant.Importance };
            byte[] image = [];
            long accepted = 0;
            long shifted = 0;
            for (int frame = 0; frame < 64; frame++)
            {
                image = renderer.Render(scene, RestirMatrix(Camera), Camera.Eye, Clear, capture: true).Pixels!;
                if (mode != LocalLightSamplingMode3D.Exact)
                {
                    var samples = renderer.ReadLightSamplesForQualification();
                    accepted += SumLane(samples, 10);
                    shifted += SumLane(samples, 11);
                }
            }
            if (mode == LocalLightSamplingMode3D.Exact)
            {
                control = image;
            }
            double error = Error(image, control!);
            errors[variant.Name] = error;
            NativeGameGraphics.WritePng(Path.Combine(output, "taa-" + variant.Name + ".png"), Width, Height, image);
            if (mode == LocalLightSamplingMode3D.ReprojectedReservoir || mode == LocalLightSamplingMode3D.SpatiotemporalReservoir)
            {
                Require(accepted > Width * Height * 20 && shifted > 0, "Projection jitter prevented meaningful temporal reservoir reuse.");
            }
            results.Add(new { Mode = variant.Name, DisplayRgbMae = error, Accepted = accepted, Shifted = shifted });
            Console.WriteLine($"ReSTIR TAA {variant.Name}: display error {error:F6}, accepted {accepted}, shifted {shifted}");
        }
        Require(errors["SpatiotemporalReservoir"] < errors["TemporalReservoir"] * .9,
            "Spatial and reprojected reuse failed to improve ordinary TAA output.");
        return new { Frames = 64, Control = "Exact native TAA with matching jitter", Results = results };
    }

    private static object CheckRestirCorrectness(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box, NativeMesh3D sphere,
        bool importanceProposals)
    {
        // One source removes all light-selection variance and exposes normalization or stale visibility errors.
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var exactTarget = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true);
        using var exact = RenderingBindlessProof.CreateRenderer(plant, assets, exactTarget, enableCulling: true, rayShadows: true);
        foreach (var item in new[] { renderer, exact })
        {
            item.EnableVisibilityBuffer = true;
            item.EnableRayShadows = true;
            item.Settings = Settings();
            item.LocalLights = [new(new(0, 3, 1), Vector3.One, 5, 8) { CastShadows = true }];
        }
        renderer.LocalLightSampling = RestirOptions(LocalLightSamplingMode3D.SpatiotemporalReservoir) with
        {
            ImportanceProposals = importanceProposals,
        };
        NativeInstanceSet3D? previous = null;
        for (int frame = 0; frame < 8; frame++)
        {
            var source = Scene(box, sphere, 0);
            var snapshot = new NativeInstanceSet3D(source.Instances.Select(instance => instance.Identity == "moving"
                ? instance with { Transform = instance.Transform * Matrix4x4.CreateTranslation(frame * .2f, 0, 0) } : instance), previous);
            var scene = source with { Instances = snapshot };
            var camera = Camera with { Eye = Camera.Eye + new Vector3(frame * .04f, 0, 0), Target = Camera.Target + new Vector3(frame * .04f, 0, 0) };
            byte[] control = exact.Render(scene, RestirMatrix(camera), camera.Eye, Clear, capture: true).Pixels!;
            byte[] actual = renderer.Render(scene, RestirMatrix(camera), camera.Eye, Clear, capture: true).Pixels!;
            Require(Error(actual, control) < .00005, "Single-source spatial/temporal normalization or current visibility differs from exact.");
            previous = snapshot;
            if (frame == 7)
            {
                try
                {
                    renderer.Render(scene, RestirMatrix(camera), camera.Eye, Clear, capture: true);
                    throw new InvalidDataException("A consumed motion snapshot was reused without a diagnostic.");
                }
                catch (InvalidOperationException failure)
                {
                    Require(failure.Message.Contains("AUR-MOTION-002"), failure.Message);
                }
            }
        }
        var cutScene = Scene(box, sphere, 0);
        renderer.ResetTemporalHistory();
        byte[] cut = renderer.Render(cutScene, RestirMatrix(Camera), Camera.Eye, Clear, capture: true).Pixels!;
        Require(SumLane(renderer.ReadLightSamplesForQualification(), 10) == 0, "Camera cut retained temporal reservoirs.");
        renderer.ResetTemporalHistory();
        byte[] repeat = renderer.Render(cutScene, RestirMatrix(Camera), Camera.Eye, Clear, capture: true).Pixels!;
        Require(cut.SequenceEqual(repeat), "Explicit cut did not reset the complete reservoir sequence.");
        byte[] exactCut = exact.Render(cutScene, RestirMatrix(Camera), Camera.Eye, Clear, capture: true).Pixels!;
        foreach (int budget in new[] { Width * Height, Width * Height + Width * Height / 3, Width * Height * 4 })
        {
            renderer.LocalLightSampling = RestirOptions(LocalLightSamplingMode3D.SpatiotemporalReservoir) with
            {
                FrameRayBudget = budget,
                ImportanceProposals = importanceProposals,
            };
            byte[] actual = renderer.Render(cutScene, RestirMatrix(Camera), Camera.Eye, Clear, capture: true).Pixels!;
            float[] samples = renderer.ReadLightSamplesForQualification();
            Require(Error(actual, exactCut) < .00005 && SumLane(samples, 9) == budget && SumLane(samples, 8) <= budget,
                "Variable-quota spatial reuse changed single-source radiance or exceeded its global cap.");
        }
        return new { ImportanceProposals = importanceProposals, VariableBudgetQualified = true, MovingSingleSourceQualified = true, CameraCutReset = true };
    }
}
