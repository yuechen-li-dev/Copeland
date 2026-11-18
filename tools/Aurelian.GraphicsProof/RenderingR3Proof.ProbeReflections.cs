using System.Numerics;
using System.Text.Json;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    private const uint ProbeGuideBit = 0x80000000;
    private const int ProbeReferenceSamples = 512;
    private static readonly DiffuseProbeGrid ReflectionProbeGrid = new(new(-1.9f, .32f, -2.2f), new(1.9f, 1.8f, 2.2f), 8, 3, 8, 128);

    public static void RunProbeReflections(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidencePath, "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R5c probe-guided glossy reflections"));
        Require(initialized.Success, "Probe reflection plant initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var source = new EnvironmentRadiance3D(1, 1, [ReflectionSky.X, ReflectionSky.Y, ReflectionSky.Z, 1]);
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        Matrix4x4 camera = ReflectionCamera(ReflectionWidth, ReflectionHeight);
        var scene = BrightCardProbeScene(SetReflectionRoughness(ReflectionScene(curved: false), .36f));
        using var referenceTarget = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var referenceRenderer = ProbeReflectionRenderer(plant, assets, referenceTarget, source, environment);
        referenceRenderer.Settings = referenceRenderer.Settings with { GlossyReflections = GlossyReflectionMode3D.Raw };
        var (reference, _, _, _) = AccumulateGlossy(referenceRenderer, scene, camera, ProbeReferenceSamples);
        SaveSamplingImage(plant, assets, output, "bright-36-reference", reference);
        var quality = new List<object>();
        var pairs = new List<object> { new { Name = "identity", Reference = "bright-36-reference.png", Test = "bright-36-reference.png", Group = "identity" } };
        foreach (var (name, mode) in new[] { ("full", GlossyReflectionMode3D.FullResolution), ("quarter", GlossyReflectionMode3D.HalfResolution) })
        {
            var baseline = MeasureGuideQuality(plant, assets, output, source, environment, scene, camera, reference, name + "-hardware", mode, false);
            var guided = MeasureGuideQuality(plant, assets, output, source, environment, scene, camera, reference, name + "-guided", mode, true);
            Require(guided.Counts.Attempts == baseline.Counts.Attempts && guided.Counts.Guided > guided.Counts.Attempts * .5,
                "The probe guide did not participate at the same glossy sample budget as hardware.");
            Require(guided.Counts.SurfaceHits > 50 && guided.Counts.SkyMisses > 100,
                "A guided ray did not produce real hit and miss packets.");
            Require(guided.MeanRmse < baseline.MeanRmse * .85 && guided.Rmse < baseline.Rmse * .85
                && guided.MeanFrameDelta < baseline.MeanFrameDelta * .8,
                "The guide did not reduce HDR error and frame-to-frame variation at the same ray rate.");
            quality.Add(new { Mode = name, Hardware = baseline, Guided = guided });
            foreach (string variant in new[] { "hardware", "guided" })
            {
                string capture = name + "-" + variant;
                pairs.Add(new { Name = capture, Reference = "bright-36-reference.png", Test = capture + ".png", Group = capture });
            }
        }
        object fallback = CheckProbeGuideFallback(plant, assets, source, environment, scene, camera);
        object motion = CheckProbeGuideMotion(plant, assets, output, source, environment, scene, camera);
        object performance = MeasureProbeReflections(plant, assets, source, environment, scene);
        validation.RequireClean();
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        {
            Qualification = "probe-guided-reflections", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs,
        }, json));
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, ReferenceSamples = ProbeReferenceSamples,
            Quality = quality, Fallback = fallback, Motion = motion, Performance = performance,
        }, json));
        Console.WriteLine("AURELIAN_R5C_PASSED " + output);
    }

    private static Native3DScene BrightCardProbeScene(Native3DScene scene) => scene with
    {
        Instances = new NativeInstanceSet3D(scene.Instances.Where(instance => instance.Identity != "orb")
            .Select(instance => instance.Identity == "checker"
                ? instance with
                {
                    Material = instance.Material with { Emissive = new(14, 8, 4), EmissiveTexture = null },
                    Transform = Matrix4x4.CreateScale(.48f) * instance.Transform,
                }
                : instance)),
    };

    private static VulkanSolid3DRenderer ProbeReflectionRenderer(AurelianVulkanPlant plant, GameAssets assets,
        VulkanNativeFrameTarget target, EnvironmentRadiance3D source, EnvironmentLighting environment)
    {
        var renderer = ReflectionRenderer(plant, assets, target, source, environment);
        renderer.Settings = renderer.Settings with { GlossyReflections = GlossyReflectionMode3D.FullResolution };
        renderer.EnableRayShadows = true;
        renderer.DynamicDiffuse = new DynamicDiffuseLighting3D
        {
            Mode = DynamicDiffuseMode3D.Runtime, Grid = ReflectionProbeGrid, RaysPerProbePerFrame = ReflectionProbeGrid.RaysPerProbe,
        };
        return renderer;
    }

    private sealed record ProbeRayCounts(int Attempts, int Guided, int SurfaceHits, int SkyMisses);
    private sealed record ProbeGuideQuality(double Rmse, double MeanRmse, double MeanFrameDelta, ProbeRayCounts Counts);

    private static ProbeGuideQuality MeasureGuideQuality(AurelianVulkanPlant plant, GameAssets assets, string output,
        EnvironmentRadiance3D source, EnvironmentLighting environment, Native3DScene scene, Matrix4x4 camera,
        ReferenceImage3D reference, string label, GlossyReflectionMode3D mode, bool guide)
    {
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ProbeReflectionRenderer(plant, assets, target, source, environment);
        renderer.Settings = renderer.Settings with { GlossyReflections = mode, GuideGlossyReflectionsWithProbes = guide };
        const int comparisons = 8;
        for (int frame = 0; frame < GlossyWarmupFrames - comparisons; frame++)
            renderer.Render(scene, camera, ReflectionEye, Clear);
        ReferenceImage3D image = reference;
        double error = 0, delta = 0;
        for (int frame = 0; frame < comparisons; frame++)
        {
            renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
            var next = renderer.ReadLightingForQualification();
            if (frame > 0) delta += Rmse(image.LinearRgb, next.LinearRgb);
            image = next;
            error += Rmse(image.LinearRgb, reference.LinearRgb);
        }
        bool scheduled = renderer.CurrentFramePlan!.Graph.Resources.Any(resource => resource.Name == "glossy-emitter-guide");
        Require(scheduled == guide, "The compiled frame did not reflect the current probe guide resource.");
        if (guide) Require(renderer.ReadProbeGuideForQualification().W > 0, "The R4 rays did not observe the small emissive card.");
        SaveSamplingImage(plant, assets, output, label, image);
        var counts = CountProbeRays(renderer.ReadGlossyReflectionsForQualification());
        var quality = new ProbeGuideQuality(Rmse(image.LinearRgb, reference.LinearRgb), error / comparisons, delta / (comparisons - 1), counts);
        Console.WriteLine($"R5c {label}: {quality}");
        return quality;
    }

    private static ProbeRayCounts CountProbeRays((float[] Radiance, uint[] HitPairs) capture)
    {
        int attempts = 0, guided = 0, hits = 0, misses = 0;
        for (int pixel = 0; pixel < capture.Radiance.Length / 4; pixel++)
        {
            if (capture.Radiance[pixel * 4 + 3] >= 0) continue;
            attempts++;
            uint packet = capture.HitPairs[pixel * 2 + 1];
            if ((packet & ProbeGuideBit) == 0) continue;
            guided++;
            uint instance = capture.HitPairs[pixel * 2];
            uint distance = packet & ~ProbeGuideBit;
            if (instance == 0)
            {
                Require(distance == 0, "A guided sky ray carried a surface distance.");
                misses++;
            }
            else
            {
                Require(instance == 2 && distance is > 200 and < 1200,
                    "The guided hardware hit escaped the emitter or the fixture distance range.");
                hits++;
            }
        }
        return new(attempts, guided, hits, misses);
    }

    private static object CheckProbeGuideFallback(AurelianVulkanPlant plant, GameAssets assets, EnvironmentRadiance3D source,
        EnvironmentLighting environment, Native3DScene bright, Matrix4x4 camera)
    {
        var cases = new List<object>();
        var unobserved = bright with
        {
            Instances = new NativeInstanceSet3D(bright.Instances.Select(instance => instance.Identity == "checker"
                ? instance with { Transform = instance.Transform * Matrix4x4.CreateTranslation(-100f, 0, 0) }
                : instance)),
        };
        foreach (var (name, scene, budget, expectedGuide) in new[]
        {
            ("near-24", SetReflectionRoughness(ReflectionScene(false), .24f), 128, false),
            ("near-44", SetReflectionRoughness(ReflectionScene(false), .44f), 128, false),
            ("curved-36", SetReflectionRoughness(ReflectionScene(true), .36f), 128, false),
            ("unobserved-emitter", unobserved, 128, true),
            ("partial-field", bright, 32, false),
            ("compiled-field", bright, -1, false),
            ("field-off", bright, 0, false),
        })
        {
            using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
            using var baselineTarget = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
            using var renderer = ProbeReflectionRenderer(plant, assets, target, source, environment);
            using var baseline = ProbeReflectionRenderer(plant, assets, baselineTarget, source, environment);
            renderer.Settings = renderer.Settings with { GuideGlossyReflectionsWithProbes = true };
            var diffuse = renderer.DynamicDiffuse;
            if (budget == -1)
            {
                baseline.Render(scene, camera, ReflectionEye, Clear);
                renderer.Render(scene, camera, ReflectionEye, Clear);
                var compiled = baseline.CaptureCompiledDiffuseProbes();
                renderer.SetCompiledDiffuseProbes(compiled);
                baseline.SetCompiledDiffuseProbes(compiled);
                renderer.ResetTemporalHistory();
                baseline.ResetTemporalHistory();
                diffuse = diffuse with { Mode = DynamicDiffuseMode3D.CompiledOnly };
            }
            else if (budget == 0) diffuse = new DynamicDiffuseLighting3D();
            else diffuse = diffuse with { RaysPerProbePerFrame = budget };
            renderer.DynamicDiffuse = baseline.DynamicDiffuse = diffuse;
            for (int frame = 0; frame < 8; frame++)
            {
                renderer.Render(scene, camera, ReflectionEye, Clear, capture: frame == 7);
                baseline.Render(scene, camera, ReflectionEye, Clear, capture: frame == 7);
            }
            bool scheduled = renderer.CurrentFramePlan!.Graph.Resources.Any(resource => resource.Name == "glossy-emitter-guide");
            Require(scheduled == expectedGuide, "Unexpected guide scheduling for " + name);
            if (scheduled) Require(renderer.ReadProbeGuideForQualification().W == 0, "An unobserved emitter unexpectedly published a guide.");
            var expected = baseline.ReadLightingForQualification();
            var actual = renderer.ReadLightingForQualification();
            Require(expected.LinearRgb.SequenceEqual(actual.LinearRgb), "Guide fallback changed hardware lighting for " + name);
            if (budget != 0) Require(renderer.ReadDiffuseOutputForQualification().SequenceEqual(baseline.ReadDiffuseOutputForQualification()),
                "The guide modified the existing R4 diffuse output for " + name);
            cases.Add(new { Name = name, GuideScheduled = scheduled, HdrMatchesHardware = true });
        }
        return cases;
    }

    private static object CheckProbeGuideMotion(AurelianVulkanPlant plant, GameAssets assets, string output, EnvironmentRadiance3D source,
        EnvironmentLighting environment, Native3DScene scene, Matrix4x4 camera)
    {
        using var target = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var coldTarget = new VulkanNativeFrameTarget(plant, ReflectionWidth, ReflectionHeight);
        using var renderer = ProbeReflectionRenderer(plant, assets, target, source, environment);
        using var cold = ProbeReflectionRenderer(plant, assets, coldTarget, source, environment);
        renderer.Settings = renderer.Settings with { GuideGlossyReflectionsWithProbes = true };
        cold.Settings = renderer.Settings;
        Vector3 shift = new(-.7f, 0, 0);
        Matrix4x4 priorCamera = Matrix4x4.CreateTranslation(-shift) * camera;
        for (int frame = 0; frame < GlossyWarmupFrames; frame++)
        {
            renderer.Render(scene, priorCamera, ReflectionEye + shift, Clear);
            cold.Render(scene, priorCamera, ReflectionEye + shift, Clear);
        }
        cold.ResetTemporalHistory();
        renderer.Render(scene, camera, ReflectionEye, Clear, capture: true);
        cold.Render(scene, camera, ReflectionEye, Clear, capture: true);
        var reprojected = renderer.ReadLightingForQualification();
        var reset = cold.ReadLightingForQualification();
        int disoccluded = 0;
        for (int y = 35; y < 104; y++)
        {
            for (int x = 205; x < 228; x++)
            {
                int offset = (y * ReflectionWidth + x) * 3;
                if (reset.LinearRgb[offset] <= 0) continue;
                Require(reprojected.LinearRgb.AsSpan(offset, 3).SequenceEqual(reset.LinearRgb.AsSpan(offset, 3)),
                    "Newly exposed guided reflection pixels inherited old history.");
                disoccluded++;
            }
        }
        Require(disoccluded > 500 && Rmse(reprojected.LinearRgb, reset.LinearRgb) > .005,
            "The camera replay did not exercise disocclusion and visible history together.");
        var before = renderer.ReadProbeGuideForQualification();
        var instances = (NativeInstanceSet3D)scene.Instances;
        var moved = scene with
        {
            Instances = new NativeInstanceSet3D(instances.Select(instance => instance.Identity == "checker"
                ? instance with { Transform = instance.Transform * Matrix4x4.CreateTranslation(-1.2f, 0, 0) }
                : instance), instances),
        };
        cold.ResetTemporalHistory();
        var movedResult = renderer.Render(moved, camera, ReflectionEye, Clear, capture: true);
        var resetResult = cold.Render(moved, camera, ReflectionEye, Clear, capture: true);
        var after = renderer.ReadProbeGuideForQualification();
        Require(before.W > 0 && after.W > 0 && Math.Abs(after.X - before.X) > .8f
            && renderer.RaySceneStatistics is { TopLevelBuilds: 1, TopLevelUpdates: 1 },
            "A moving off-camera emitter did not publish a fresh same-frame guide after refit.");
        Require(movedResult.Pixels!.SequenceEqual(resetResult.Pixels!), "The guided reflection retained stale emitter history.");
        SaveSamplingImage(plant, assets, output, "motion-before-emitter", reprojected);
        SaveSamplingImage(plant, assets, output, "motion-after-emitter", renderer.ReadLightingForQualification());
        return new { DisoccludedPixelsMatchCold = disoccluded, GuideCenterShiftMeters = Math.Abs(after.X - before.X), MatchesExplicitReset = true,
            Statistics = renderer.RaySceneStatistics };
    }

    private static object MeasureProbeReflections(AurelianVulkanPlant plant, GameAssets assets, EnvironmentRadiance3D source,
        EnvironmentLighting environment, Native3DScene bright)
    {
        using var game = new BeaconGame();
        var publisher = new BeaconResidentScene3D();
        var arena = publisher.Build(game, includeViewmodel: false);
        arena = arena with
        {
            Instances = new NativeInstanceSet3D(arena.Instances.Select(instance => instance.Identity.Contains("/floor/", StringComparison.Ordinal)
                ? instance with { Material = instance.Material with { Roughness = .36f } }
                : instance)),
        };
        var beaconEnvironment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
        var camera = new ReferenceCamera3D(new(4, 5.5f, 9), new(0, .4f, -1), Vector3.UnitY, .82f);
        var rows = new List<object>();
        foreach (int width in new[] { 1280, 1920 })
        {
            int height = width * 9 / 16;
            using var target = new VulkanNativeFrameTarget(plant, (uint)width, (uint)height);
            foreach (bool beacon in new[] { false, true })
            {
                using var renderer = beacon ? BeaconRenderer(plant, assets, target, beaconEnvironment)
                    : ProbeReflectionRenderer(plant, assets, target, source, environment);
                if (beacon) renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
                var clip = beacon ? RegionalMatrix(camera, width, height) : ReflectionCamera(width, height);
                var eye = beacon ? camera.Eye : ReflectionEye;
                var scene = beacon ? arena : bright;
                renderer.Settings = renderer.Settings with { GlossyReflections = GlossyReflectionMode3D.HalfResolution };
                foreach (bool guide in new[] { false, true })
                {
                    renderer.Settings = renderer.Settings with { GuideGlossyReflectionsWithProbes = guide };
                    object timing = CollectReflectionTiming(_ => renderer.Render(scene, clip, eye, Clear));
                    renderer.WaitForPendingFrames();
                    var measured = renderer.Render(scene, clip, eye, Clear, capture: true);
                    double trace = measured.GpuPassTimes.Single(pass => pass.Pass == "sharp-reflections").Milliseconds;
                    string workload = beacon ? "beacon-gi" : "bright-card-gi";
                    rows.Add(new { Width = width, Height = height, Workload = workload, Guided = guide, Timing = timing, TraceMilliseconds = trace });
                    Console.WriteLine($"R5c {width} {workload} guided={guide}: {timing}; trace {trace:F3}ms.");
                }
            }
        }
        return rows;
    }
}
