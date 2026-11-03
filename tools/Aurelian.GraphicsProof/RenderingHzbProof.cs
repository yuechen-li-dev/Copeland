using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingHzbProof
{
    private const int Width = 640;
    private const int Height = 360;
    private static readonly NativeFrameClearColor Clear = new(.1f, .15f, .22f, 1);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, ApplicationName: "Aurelian two-phase HZB"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        Require(plant.Facts.HierarchicalZEnabled, "Device does not admit two-phase HZB.");
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true);
        renderer.EnableVisibilityBuffer = true;
        renderer.FrameMode = Native3DFrameMode.Interactive;
        renderer.Settings = Graphics3DSettings.Default with
        {
            Shadows = false, AntiAliasing = AntiAliasing3D.None, BloomIntensity = 0, AmbientOcclusionStrength = 0,
        };
        var forest = Forest(false);
        var occluded = Forest(true);
        var eye = new Vector3(0, 3, 8);
        var camera = RenderingBindlessProof.Camera(eye, new(0, 1.4f, -20));
        var comparisons = new List<object>();
        var measurements = new List<Measurement>();
        foreach (var specimen in new[] { (Name: "occluded-forest", Scene: occluded),
            (Name: "dense-occluded-forest", Scene: Forest(true, dense: true)), (Name: "open-forest", Scene: forest) })
        {
            renderer.EnableHierarchicalZ = false;
            var reference = renderer.Render(specimen.Scene, camera, eye, Clear, capture: true);
            var referenceIds = renderer.ReadIdentitiesForQualification();
            var controlGroups = renderer.ReadVisibilityForQualification();
            renderer.EnableHierarchicalZ = true;
            for (int frame = 0; frame < 4; frame++)
            {
                var actual = renderer.Render(specimen.Scene, camera, eye, Clear, capture: true);
                var actualIds = renderer.ReadIdentitiesForQualification();
                Require(referenceIds.Ids.SequenceEqual(actualIds.Ids), specimen.Name + ": HZB changed native identity coverage.");
                Require(reference.PixelSha256 == actual.PixelSha256, specimen.Name + ": HZB changed captured pixels.");
                var groups = renderer.ReadVisibilityForQualification();
                var pyramid = renderer.ReadHzbForQualification();
                CheckPyramid(pyramid);
                var classification = CheckOcclusion(groups, pyramid, camera);
                comparisons.Add(new { specimen.Name, Frame = frame, actual.PixelSha256,
                    ControlDraws = controlGroups.Sum(group => group.DrawCount), SeedDraws = groups.Sum(group => group.SeedMask.Count(value => value != 0)),
                    FinalDraws = groups.Sum(group => group.DrawCount), Classification = classification, actual.Retained });
                Console.WriteLine($"{specimen.Name}, frame {frame}: seed {groups.Sum(group => group.SeedMask.Count(value => value != 0))}, final {groups.Sum(group => group.DrawCount)}, reference {controlGroups.Sum(group => group.DrawCount)}");
                if (frame == 3) NativeGameGraphics.WritePng(Path.Combine(output, specimen.Name + ".png"), Width, Height, actual.Pixels!);
            }
            File.WriteAllText(Path.Combine(output, specimen.Name + ".frame-plan.json"), renderer.CurrentFramePlan!.Describe());
            Measure(renderer, specimen.Scene, camera, eye, specimen.Name, measurements);
        }
        var boundaries = CheckBoundaries(plant, assets, output);
        var denseSamples = measurements.Where(measurement => measurement.Name == "dense-occluded-forest").ToArray();
        Require(denseSamples.Where(sample => sample.Hzb).Max(sample => sample.GpuMilliseconds)
            < denseSamples.Where(sample => !sample.Hzb).Min(sample => sample.GpuMilliseconds) * .75,
            "Dense occlusion did not improve complete GPU frame time by at least 25% in both ABBA samples.");
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts, WarmupFrames = 128, MeasuredFrames = 96,
            Comparisons = comparisons, Measurements = measurements, Boundaries = boundaries,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_HZB_PASSED " + output);
    }

    private static Native3DScene Forest(bool wall, bool dense = false, bool mask = false)
    {
        var mesh = RenderingR2Proof.Tree();
        var materials = RenderingBindlessProof.Materials(64);
        var instances = new List<SceneInstance3D>();
        if (wall)
        {
            var quad = new NativeMesh3D("hzb-wall", [
                new(new(-20, -20, 3), Vector3.UnitZ, Vector4.One, new(0, 0), new(1, 0, 0, 1)),
                new(new(20, -20, 3), Vector3.UnitZ, Vector4.One, new(1, 0), new(1, 0, 0, 1)),
                new(new(20, 20, 3), Vector3.UnitZ, Vector4.One, new(1, 1), new(1, 0, 0, 1)),
                new(new(-20, 20, 3), Vector3.UnitZ, Vector4.One, new(0, 1), new(1, 0, 0, 1)),
            ], [0, 1, 2, 0, 2, 3]);
            var material = new ModelMaterial("wall") { BaseColor = new(.3f, .4f, .5f, 1), DoubleSided = true };
            if (mask)
            {
                byte[] texels = [255, 255, 255, 255, 255, 255, 255, 0, 255, 255, 255, 0, 255, 255, 255, 255];
                material = material with { AlphaMask = true, BaseColorTexture = new(new("wall-holes", 2, 2,
                    System.Collections.Immutable.ImmutableArray.Create(texels)), new ModelSampler(false, false)) };
            }
            instances.Add(new("wall", quad, material, Matrix4x4.Identity));
        }
        instances.AddRange(Enumerable.Range(0, 20_000).Select(index => new SceneInstance3D("tree-" + index, mesh,
            materials[index % materials.Length], Matrix4x4.CreateScale(.8f + index % 7 * .07f)
                * Matrix4x4.CreateRotationY(index * .71f)
                * Matrix4x4.CreateTranslation((index % 200 - 100) * (dense ? .04f : 2.5f), 0,
                    -(index / 200) * (dense ? .04f : 2.5f)))));
        return new([], []) { Instances = new NativeInstanceSet3D(instances) };
    }

    private static void CheckPyramid(HzbPyramidReadback pyramid)
    {
        foreach (float depth in pyramid.Seed.Concat(pyramid.History))
            Require(float.IsFinite(depth) && depth is >= 0 and <= 1, "HZB contains an invalid depth.");
        foreach (float[] values in new[] { pyramid.Seed, pyramid.History })
        {
            for (int level = 1; level < pyramid.Widths.Length; level++)
            {
                for (uint y = 0; y < pyramid.Heights[level]; y++)
                {
                    for (uint x = 0; x < pyramid.Widths[level]; x++)
                    {
                        float maximum = 0;
                        for (uint dy = 0; dy < 2; dy++)
                        {
                            for (uint dx = 0; dx < 2; dx++)
                            {
                                uint sx = Math.Min(x * 2 + dx, pyramid.Widths[level - 1] - 1);
                                uint sy = Math.Min(y * 2 + dy, pyramid.Heights[level - 1] - 1);
                                maximum = Math.Max(maximum, values[pyramid.Offsets[level - 1] + sy * pyramid.Widths[level - 1] + sx]);
                            }
                        }
                        Require(values[pyramid.Offsets[level] + y * pyramid.Widths[level] + x] == maximum,
                            "GPU max-depth pyramid disagrees with independent CPU reduction.");
                    }
                }
            }
        }
    }

    private sealed record Measurement(string Name, bool Hzb, double CpuMilliseconds, double GpuMilliseconds,
        double FramesPerSecond, Dictionary<string, double> Passes);

    private static void Measure(VulkanSolid3DRenderer renderer, Native3DScene scene, Matrix4x4 camera, Vector3 eye,
        string name, List<Measurement> measurements)
    {
        foreach (bool hzb in new[] { false, true, true, false })
        {
            renderer.EnableHierarchicalZ = hzb;
            renderer.Render(scene, camera, eye, Clear, capture: true);
            var cpu = new List<double>();
            var gpu = new List<double>();
            var passes = new Dictionary<string, List<double>>();
            long started = 0;
            for (int frame = 0; frame < 224; frame++)
            {
                if (frame == 128) started = Stopwatch.GetTimestamp();
                var result = renderer.Render(scene, camera, eye, Clear);
                if (frame < 128) continue;
                cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds);
                if (result.CompletedGpuFrame is { } completed)
                    gpu.Add((completed.EndTicks - completed.StartTicks) * completed.NanosecondsPerTick / 1_000_000d);
                foreach (var pass in result.GpuPassTimes)
                {
                    if (!passes.TryGetValue(pass.Pass, out var samples)) passes.Add(pass.Pass, samples = []);
                    samples.Add(pass.Milliseconds);
                }
                Require(result.Retained is { CpuVisitedInstances: 0, InstanceUploadBytes: 0 }, "HZB revisited resident instances on the CPU.");
            }
            renderer.WaitForPendingFrames();
            double fps = 96 / Stopwatch.GetElapsedTime(started).TotalSeconds;
            measurements.Add(new(name, hzb, Median(cpu), Median(gpu), fps,
                passes.ToDictionary(pair => pair.Key, pair => Median(pair.Value))));
            Console.WriteLine($"{name}, HZB={hzb}: CPU {Median(cpu):F3}, GPU {Median(gpu):F3} ms, {fps:F1} FPS");
        }
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
