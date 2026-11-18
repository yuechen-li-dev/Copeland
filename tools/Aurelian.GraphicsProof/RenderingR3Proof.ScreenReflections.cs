using System.Numerics;
using System.Text.Json;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private const uint ScreenCandidate = 0x40000000;
    private const uint ScreenRetry = 0x20000000;
    private const uint ReflectionDistance = 0x1fffffff;
    private static readonly ReferenceCamera3D ScreenCamera = new(new(0, 2.1f, 5.2f), new(0, .75f, -2.8f), Vector3.UnitY, .8f);

    public static void RunScreenReflections(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R5d screen-assisted hardware reflections"));
        Require(initialized.Success, "Screen reflection plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var source = new EnvironmentRadiance3D(1, 1, [ReflectionSky.X, ReflectionSky.Y, ReflectionSky.Z, 1]);
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        var reflected = ScreenReflectionScene(16, .24f);
        var ordinary = ScreenReflectionScene(1, .24f);
        var offscreen = SetReflectionRoughness(ReflectionScene(false), .24f);
        var quality = new List<object>();
        var pairs = new List<object>();
        foreach (var (name, scene, camera, eye, rate) in new[]
        {
            ("visible", ordinary, RegionalMatrix(ScreenCamera, ReflectionWidth, ReflectionHeight), ScreenCamera.Eye, GlossyReflectionMode3D.FullResolution),
            ("layered", reflected, RegionalMatrix(ScreenCamera, ReflectionWidth, ReflectionHeight), ScreenCamera.Eye, GlossyReflectionMode3D.FullResolution),
            ("layered-quarter", reflected, RegionalMatrix(ScreenCamera, ReflectionWidth, ReflectionHeight), ScreenCamera.Eye, GlossyReflectionMode3D.HalfResolution),
            ("backface-near-tie", WithHiddenScreenBlocker(ordinary), RegionalMatrix(ScreenCamera, ReflectionWidth, ReflectionHeight), ScreenCamera.Eye, GlossyReflectionMode3D.FullResolution),
            ("ray-filtered", WithoutFrontRayMask(reflected), RegionalMatrix(ScreenCamera, ReflectionWidth, ReflectionHeight), ScreenCamera.Eye, GlossyReflectionMode3D.FullResolution),
            ("offscreen", offscreen, ReflectionCamera(ReflectionWidth, ReflectionHeight), ReflectionEye, GlossyReflectionMode3D.FullResolution),
        })
        {
            using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
            ReferenceImage3D? reference = null;
            (float[] Radiance, uint[] HitPairs) hardware = default;
            foreach (bool screenBounds in new[] { false, true })
            {
                using var renderer = ReflectionRenderer(plant, assets, target, source, environment);
                renderer.Settings = renderer.Settings with { GlossyReflections = rate, ScreenSpaceReflectionBounds = screenBounds };
                for (int frame = 0; frame < 24; frame++)
                    renderer.Render(scene, camera, eye, Clear, capture: frame == 23);
                var image = renderer.ReadLightingForQualification();
                var hits = renderer.ReadGlossyReflectionsForQualification();
                if (!screenBounds)
                {
                    reference = image;
                    hardware = hits;
                }
                var counts = CompareScreenHits(hardware, hits);
                double error = Rmse(reference!.LinearRgb, image.LinearRgb);
                Require(error == 0 && counts.IdentityDifferences == 0 && counts.DistanceDifferences == 0,
                    "Screen-assisted hardware differed from the baseline in " + name);
                if (screenBounds && name != "offscreen") Require(counts.Candidates > counts.Attempts * .25,
                    "The screen candidate lane did not exercise bounded rays in " + name);
                if (screenBounds && name == "offscreen") Require(counts.HardwareFallback == counts.Attempts && counts.SurfaceHits > 500,
                    "Off-screen hits did not use full-range hardware.");
                if (screenBounds && name == "ray-filtered") Require(counts.RetriesWithHit > 500,
                    "The ray-filtered screen surface did not exercise successful full-range retries.");
                string label = name + (screenBounds ? "-screen" : "-hardware");
                SaveSamplingImage(plant, assets, output, label, image);
                pairs.Add(new { Name = label, Reference = name + "-hardware.png", Test = label + ".png", Group = label });
                quality.Add(new { Scene = name, ScreenBounds = screenBounds, HdrRmse = error, Hits = counts });
                Console.WriteLine($"R5d {label}: HDR RMSE {error:F6}. {counts}");
            }
        }
        pairs.Add(new { Name = "identity", Reference = "visible-hardware.png", Test = "visible-hardware.png", Group = "identity" });
        object motion = CheckScreenReflectionMotion(plant, assets, output, source, environment, reflected, offscreen);
        object performance = MeasureScreenReflections(plant, assets, source, environment, ordinary, reflected, offscreen);
        validation.RequireClean();
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "screen-reflection-bounds", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, options));
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Quality = quality, Motion = motion, Performance = performance,
        }, options));
        Console.WriteLine("AURELIAN_R5D_PASSED " + output);
    }

    private static Native3DScene WithHiddenScreenBlocker(Native3DScene scene)
    {
        var wall = scene.Instances.Single(instance => instance.Identity == "wall-0");
        var blocker = wall with
        {
            Identity = "back-facing-blocker",
            Mesh = ReflectionQuad("screen-back-facing-blocker", -Vector3.UnitX * 4, Vector3.UnitY * 2),
            Material = new ModelMaterial("back-facing-blocker") { BaseColor = new(0, 0, 0, 1), Roughness = .8f },
            Transform = wall.Transform * Matrix4x4.CreateTranslation(0, 0, .0004f),
        };
        return scene with { Instances = new NativeInstanceSet3D(scene.Instances.Append(blocker)) };
    }

    private static Native3DScene ScreenReflectionScene(int layers, float roughness)
    {
        var seed = ReflectionScene(false);
        var floor = new SceneInstance3D("floor", ReflectionQuad("screen-floor", Vector3.UnitX * 4, -Vector3.UnitZ * 6),
            seed.Instances.First().Material with { Roughness = roughness }, Matrix4x4.Identity);
        var wall = ReflectionQuad("screen-wall", Vector3.UnitX * 4, Vector3.UnitY * 2);
        var material = seed.Instances.Single(instance => instance.Identity == "checker").Material;
        var instances = new List<SceneInstance3D> { floor };
        for (int index = 0; index < layers; index++)
        {
            instances.Add(new($"wall-{index}", wall, material, Matrix4x4.CreateTranslation(0, 2, -2.8f - index * .2f)));
        }
        return new([], []) { Instances = new NativeInstanceSet3D(instances) };
    }

    private static Native3DScene WithoutFrontRayMask(Native3DScene scene) => scene with
    {
        Instances = new NativeInstanceSet3D(scene.Instances.Select(instance => instance.Identity == "wall-0"
            ? instance with { RayMask = 0 }
            : instance)),
    };

    private sealed record ScreenHitCounts(int Attempts, int Candidates, int Retries, int RetriesWithHit, int HardwareFallback, int SurfaceHits,
        int IdentityDifferences, int DistanceDifferences, double MaximumDistanceDeltaMeters);

    private static ScreenHitCounts CompareScreenHits((float[] Radiance, uint[] HitPairs) hardware, (float[] Radiance, uint[] HitPairs) variant)
    {
        int attempts = 0, candidates = 0, retries = 0, retriesWithHit = 0, fallback = 0, hits = 0, identityDifferences = 0, distanceDifferences = 0;
        double maxDelta = 0;
        for (int pixel = 0; pixel < hardware.Radiance.Length / 4; pixel++)
        {
            uint expected = hardware.HitPairs[pixel * 2 + 1] & ReflectionDistance;
            if (hardware.Radiance[pixel * 4 + 3] >= 0 || expected == 1) continue;
            attempts++;
            uint packet = variant.HitPairs[pixel * 2 + 1];
            if ((packet & ScreenCandidate) != 0) candidates++;
            else fallback++;
            uint observed = packet & ReflectionDistance;
            if (observed > 1) hits++;
            if ((packet & ScreenRetry) != 0)
            {
                retries++;
                if (observed > 1) retriesWithHit++;
            }
            if (hardware.HitPairs[pixel * 2] != variant.HitPairs[pixel * 2]) identityDifferences++;
            if (observed == expected) continue;
            distanceDifferences++;
            maxDelta = Math.Max(maxDelta, Math.Abs((double)expected - observed) * .01);
        }
        return new(attempts, candidates, retries, retriesWithHit, fallback, hits, identityDifferences, distanceDifferences, maxDelta);
    }

    private static object MeasureScreenReflections(AurelianVulkanPlant plant, GameAssets assets, EnvironmentRadiance3D source,
        EnvironmentLighting environment, Native3DScene ordinary, Native3DScene reflected, Native3DScene offscreen)
    {
        using var game = new BeaconGame();
        var publisher = new BeaconResidentScene3D();
        var arena = publisher.Build(game, includeViewmodel: false);
        arena = arena with
        {
            Instances = new NativeInstanceSet3D(arena.Instances.Select(instance => instance.Identity.Contains("/floor/", StringComparison.Ordinal)
                ? instance with { Material = instance.Material with { Roughness = .24f } }
                : instance)),
        };
        var beaconEnvironment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
        var beaconCamera = new ReferenceCamera3D(new(4, 5.5f, 9), new(0, .4f, -1), Vector3.UnitY, .82f);
        var rows = new List<object>();
        foreach (int width in new[] { 1280, 1920 })
        {
            int height = width * 9 / 16;
            using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
            foreach (var (name, scene, camera) in new[]
            {
                ("visible", ordinary, ScreenCamera), ("layered", reflected, ScreenCamera),
                ("offscreen", offscreen, ScreenCamera), ("beacon-gi", arena, beaconCamera),
            })
            {
                using var renderer = name == "beacon-gi" ? BeaconRenderer(plant, assets, target, beaconEnvironment)
                    : ReflectionRenderer(plant, assets, target, source, environment);
                if (name == "beacon-gi") renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
                Matrix4x4 clip = name == "offscreen" ? ReflectionCamera(width, height) : RegionalMatrix(camera, width, height);
                Vector3 eye = name == "offscreen" ? ReflectionEye : camera.Eye;
                bool[] passes = [false, true, true, false];
                for (int trial = 0; trial < passes.Length; trial++)
                {
                    bool screenBounds = passes[trial];
                    renderer.Settings = renderer.Settings with { GlossyReflections = GlossyReflectionMode3D.HalfResolution, ScreenSpaceReflectionBounds = screenBounds };
                    object timing = CollectReflectionTiming(_ => renderer.Render(scene, clip, eye, Clear));
                    renderer.WaitForPendingFrames();
                    var measured = renderer.Render(scene, clip, eye, Clear, capture: true);
                    double trace = measured.GpuPassTimes.Single(pass => pass.Pass == "sharp-reflections").Milliseconds;
                    rows.Add(new { Width = width, Height = height, Workload = name, Trial = trial, ScreenBounds = screenBounds, Timing = timing, TraceMilliseconds = trace });
                    Console.WriteLine($"R5d {width} {name} screen={screenBounds} trial={trial}: {timing}; trace {trace:F3}ms.");
                }
            }
        }
        return rows;
    }
}
