using System.Diagnostics;
using System.Numerics;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private static object CheckBeaconMotion(AurelianVulkanPlant plant, GameAssets assets, EnvironmentLighting environment,
        CompiledDiffuseProbeField3D field, string output, List<object> pairs)
    {
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = BeaconRenderer(plant, assets, target, environment);
        var full = new List<ReferenceImage3D>();
        var referenceFrames = new Dictionary<int, ReferenceImage3D>();
        var rows = new List<object>();
        var errors = new Dictionary<string, double>();
        string? hash = null;
        foreach (string name in new[] { "full-128", "fixed-32", "regional", "compiled" })
        {
            using var game = new BeaconGame();
            var publisher = new BeaconResidentScene3D();
            var policy = BeaconDiffuseLighting3D.Policy();
            renderer.Settings = BeaconDiffuseLighting3D.Settings;
            renderer.LocalLights = BeaconDiffuseLighting3D.Lights;
            renderer.SetCompiledDiffuseProbes(field);
            renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime with
            {
                Mode = BeaconMode(name), RaysPerProbePerFrame = name == "full-128" ? 128 : 32,
            };
            for (int frame = 0; frame < 24; frame++)
            {
                var scene = publisher.Build(game);
                if (name == "regional") policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
                var result = renderer.Render(scene, game.Camera((float)Width / Height), game.CameraEye, Clear, capture: true);
                if (name == "regional") policy.AfterFrame(result);
            }
            var differences = new List<double>();
            int maximumWork = name switch
            {
                "full-128" => field.Grid.Count * 128,
                "fixed-32" => field.Grid.Count * 32,
                _ => 0,
            };
            for (int frame = 0; frame < 64; frame++)
            {
                game.Step(new(.2f, 0, .25f, 0, Fire: frame % 20 == 0), 1f / 60);
                var scene = publisher.Build(game);
                if (frame == 16)
                {
                    renderer.Settings = renderer.Settings with { SunIntensity = .35f };
                    renderer.LocalLights = [];
                }
                if (name == "regional")
                {
                    policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
                    maximumWork = Math.Max(maximumWork, policy.Controller.LastScheduledRayUpdates);
                    Require(policy.Controller.LastScheduledRayUpdates <= field.Grid.Count * 32, "Beacon regional update cap overflowed.");
                }
                var result = renderer.Render(scene, game.Camera((float)Width / Height), game.CameraEye, Clear, capture: true);
                if (name == "regional") policy.AfterFrame(result);
                var image = renderer.ReadLightingForQualification();
                if (name == "full-128") full.Add(image);
                else differences.Add(Rmse(image.LinearRgb, full[frame].LinearRgb));
                if (frame is 0 or 19 or 63)
                {
                    var coverage = renderer.ReadLightMotionForQualification();
                    if (name == "full-128")
                    {
                        var camera = new ReferenceCamera3D(game.CameraEye, game.CameraEye + game.Direction, Vector3.UnitY, MathF.PI / 3);
                        var lighting = ReferenceLighting3D.FromSettings(renderer.Settings, BeaconDiffuseLighting3D.Source) with { LocalLights = renderer.LocalLights.ToArray() };
                        var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene,
                            renderer.LocalLights.ToArray(), camera, samples: 1024, bounces: 2, lighting: lighting);
                        referenceFrames.Add(frame, reference);
                        SaveSamplingImage(plant, assets, output, "motion-" + frame + "-reference", PresentSkyReference(reference, coverage));
                    }
                    string capture = "motion-" + frame + "-" + name;
                    SaveSamplingImage(plant, assets, output, capture, image);
                    pairs.Add(new { Name = capture, Reference = "motion-" + frame + "-reference.png", Test = capture + ".png", Group = "motion-" + name });
                    double error = SkySurfaceError(image, referenceFrames[frame], coverage);
                    rows.Add(new { Case = name, Frame = frame, SurfaceRmse = error });
                    Console.WriteLine($"Beacon motion {name} frame {frame}: HDR {error:F6}");
                }
            }
            string semantic = game.SemanticHash();
            if (hash is null) hash = semantic;
            Require(hash == semantic, "Beacon rendering controls replayed different gameplay.");
            errors[name] = differences.Count == 0 ? 0 : differences.Average();
            rows.Add(new { Case = name, MeanRmseToFullRuntime = errors[name], MaximumUpdates = maximumWork,
                RejectedFeedback = policy.Controller.RejectedObservations, GameplayHash = semantic, Shots = game.Shots });
        }
        Require(errors["fixed-32"] < errors["compiled"] * .5 && errors["regional"] < errors["compiled"] * .5,
            "Whole-arena dynamic updates did not correct stale compiled transport under motion/source changes.");
        return new { Frames = rows, MeanRmseToFullRuntime = errors };
    }

    private static object MeasureBeaconGi(AurelianVulkanPlant plant, GameAssets assets, EnvironmentLighting environment,
        CompiledDiffuseProbeField3D field, string output)
    {
        var rows = new List<object>();
        foreach (int width in new[] { 1280, 1920 })
        {
            int height = width * 9 / 16;
            using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
            using var renderer = BeaconRenderer(plant, assets, target, environment);
            renderer.SetCompiledDiffuseProbes(field);
            foreach (bool moving in new[] { false, true })
            {
                foreach (string name in new[] { "off", "fixed-32", "full-128", "regional" })
                {
                    using var game = new BeaconGame();
                    var publisher = new BeaconResidentScene3D();
                    var policy = BeaconDiffuseLighting3D.Policy();
                    renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime with
                    {
                        Mode = BeaconMode(name), RaysPerProbePerFrame = name == "full-128" ? 128 : 32,
                    };
                    var cpu = new List<double>();
                    var gpu = new List<double>();
                    var prepare = new List<double>();
                    var slotWait = new List<double>();
                    int totalUpdates = 0;
                    var wall = Stopwatch.StartNew();
                    for (int frame = 0; frame < 104; frame++)
                    {
                        if (frame == 40) wall.Restart();
                        if (moving) game.Step(new(.1f, 0, .2f, 0), 1f / 60);
                        long started = Stopwatch.GetTimestamp();
                        var scene = publisher.Build(game);
                        if (name == "regional") policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
                        double preparation = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        var result = renderer.Render(scene, game.Camera((float)width / height), game.CameraEye, Clear);
                        if (name == "regional") policy.AfterFrame(result);
                        if (frame < 40) continue;
                        cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds + preparation);
                        slotWait.Add(result.CpuTimings.SlotWaitMilliseconds);
                        prepare.Add(preparation);
                        if (result.CompletedGpuFrame is { } complete)
                            gpu.Add((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);
                        totalUpdates += name == "regional" ? policy.Controller.LastScheduledRayUpdates
                            : name == "off" ? 0 : field.Grid.Count * (name == "full-128" ? 128 : 32);
                    }
                    renderer.WaitForPendingFrames();
                    double fps = 64 / wall.Elapsed.TotalSeconds;
                    rows.Add(new { Width = width, Height = height, Moving = moving, Case = name,
                        MedianCpuMilliseconds = Median(cpu), MedianGpuMilliseconds = Median(gpu),
                        MedianPublicationAndPolicyMilliseconds = Median(prepare), MedianSlotWaitMilliseconds = Median(slotWait),
                        MeanDirectionalUpdates = totalUpdates / 64d, FramesPerSecond = fps });
                    Console.WriteLine($"Beacon {width}x{height} {name} moving={moving}: CPU {Median(cpu):F3}ms GPU {Median(gpu):F3}ms, {fps:F1}fps");
                    var captureScene = publisher.Build(game);
                    if (name == "regional") policy.BeforeFrame(renderer, captureScene, TimeSpan.FromSeconds(1d / 60));
                    var capture = renderer.Render(captureScene, game.Camera((float)width / height), game.CameraEye, Clear, capture: true);
                    NativeGameGraphics.WritePng(Path.Combine(output, $"beacon-{width}-{name}-{moving}.png"), width, height, capture.Pixels!);
                }
            }
        }
        return rows;
    }
}
