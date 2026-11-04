using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime.Rendering;

internal static partial class RenderingR3Proof
{
    public static void RunRegional(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "R4 regional GI policy"));
        Require(initialized.Success, "Regional GI initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var box = Box();
        var camera = new ReferenceCamera3D(new(6, 11, 9), new(0, .8f, 0), Vector3.UnitY, .8f);
        var matrix = RegionalMatrix(camera, Width, Height);
        var grid = new DiffuseProbeGrid(new(-3, .3f, -3), new(3, 2.1f, 3), 4, 4, 4, 128);
        var options = new DynamicDiffuseLighting3D { Mode = DynamicDiffuseMode3D.Runtime, Grid = grid, RaysPerProbePerFrame = 128 };
        var lights = new[] { new Vector3(-2, 2.6f, -2), new(2, 2.6f, -2), new(-2, 2.6f, 2), new(2, 2.6f, 2) }
            .Select(position => new LocalLight3D(position, Vector3.One, 40, 4.5f) { CastShadows = true }).ToArray();
        lights[0] = lights[0] with { Intensity = 160 };
        var changedLights = lights.ToArray();
        changedLights[0] = changedLights[0] with { Intensity = 0 };
        var scene = RegionalScene(box, null, false);
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.Settings = Settings();
        renderer.LocalLights = lights;
        renderer.DynamicDiffuse = options;
        renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        var field = renderer.CaptureCompiledDiffuseProbes();
        renderer.SetCompiledDiffuseProbes(field);
        var moved = RegionalScene(box, null, true);
        var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), moved, changedLights, camera, samples: 2048, bounces: 2);
        SaveSamplingImage(plant, assets, output, "moved-reference", reference);
        var pairs = new List<object>();
        var rows = new List<object>();
        var endpoints = new Dictionary<string, float[]>();
        var firstErrors = new Dictionary<string, double>();
        double staleError = 0;
        int maxWork = 0;
        object? policyTrace = null;
        foreach (string name in new[] { "compiled", "fixed-32", "full-128", "regional" })
        {
            renderer.DynamicDiffuse = options with { Mode = name == "full-128" ? DynamicDiffuseMode3D.Runtime : DynamicDiffuseMode3D.CompiledResidual, RaysPerProbePerFrame = name == "full-128" ? 128 : 32 };
            var policy = new NativeDiffuseRegionalPolicy3D(grid, settings: new() { MaximumRayUpdates = 2048, AuditSeconds = 5 });
            renderer.LocalLights = lights;
            scene = RegionalScene(box, null, false);
            int admissions = 0;
            for (int frame = 0; frame < 40; frame++)
            {
                scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, false);
                if (name == "regional") policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
                if (name == "compiled") renderer.DynamicDiffuse = options with { Mode = DynamicDiffuseMode3D.CompiledOnly };
                var result = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
                if (name == "regional") policy.AfterFrame(result);
                admissions = result.Retained!.TopologyAdmissions;
            }
            if (name == "regional")
            {
                Require(policy.Controller.States.All(state => state.Selected == DiffuseRegionStrategy3D.Compiled), "Stable regional field did not settle on compiled lighting.");
                Require(policy.Controller.LastScheduledRayUpdates == 0, "Stable compiled regions still traced cache updates.");
            }
            renderer.LocalLights = changedLights;
            for (int frame = 0; frame < 8; frame++)
            {
                scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, true);
                if (name == "regional")
                {
                    policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60), frame == 0 ? [0] : null);
                    maxWork = Math.Max(maxWork, policy.Controller.LastScheduledRayUpdates);
                    Require(policy.Controller.LastScheduledRayUpdates <= 2048, "Regional work exceeded its hard cap.");
                    Require(policy.LastPlan!.Regions.Skip(1).All(command => command.Strategy == DiffuseRegionStrategy3D.Compiled), "Unchanged rooms lost compiled reuse.");
                }
                var result = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
                if (name == "regional")
                {
                    policy.AfterFrame(result);
                    Require(result.Retained is { UploadedMeshes: 0 } && result.Retained.TopologyAdmissions == admissions, "Regional switching readmitted resident geometry.");
                }
                var image = renderer.ReadLightingForQualification();
                double error = Rmse(image.LinearRgb, reference.LinearRgb);
                rows.Add(new { Case = name, Frame = frame, Rmse = error,
                    RayUpdates = name == "regional" ? policy.Controller.LastScheduledRayUpdates : name == "compiled" ? 0 : name == "fixed-32" ? 2048 : 8192 });
                if (frame == 0)
                {
                    firstErrors[name] = error;
                    if (name == "compiled") staleError = error;
                }
                if (frame == 0 || frame == 7)
                {
                    string capture = name + "-" + frame;
                    SaveSamplingImage(plant, assets, output, capture, image);
                    pairs.Add(new { Name = capture, Reference = "moved-reference.png", Test = capture + ".png", Group = capture });
                }
                if (frame == 7) endpoints[name] = image.LinearRgb;
                Console.WriteLine($"Regional {name} frame {frame}: {error:F6}");
            }
            if (name == "regional")
            {
                policyTrace = new { policy.Controller.States, Trace = policy.Controller.Inspector.Observe(), policy.Controller.RejectedObservations };
                // A source change is global even when a caller supplies a local influence hint.
                renderer.Settings = renderer.Settings with { SunIntensity = .01f };
                scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, true);
                policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60), [0]);
                Require(policy.Controller.States.All(state => !state.CompleteCoverage), "Global source change failed to invalidate every region.");
                var refreshed = new HashSet<int>();
                for (int frame = 0; frame < 4; frame++)
                {
                    if (frame > 0)
                    {
                        scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, true);
                        policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
                    }
                    for (int region = 0; region < 4; region++)
                        if (policy.LastPlan!.Regions[region].RaysPerProbe == 128) refreshed.Add(region);
                    var result = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
                    policy.AfterFrame(result);
                }
                Require(refreshed.Count == 4, "Global invalidation starved a region under the real GPU feedback latency.");
                renderer.Settings = Settings();
            }
        }
        double equivalent = Rmse(endpoints["regional"], endpoints["full-128"]);
        Require(equivalent < .001, $"Regional full refresh differs from full runtime basis: {equivalent}.");
        Require(firstErrors["regional"] < firstErrors["fixed-32"], "Regional allocation did not improve first-frame reference error at equal directional work.");
        Require(firstErrors["regional"] < staleError, "Regional refresh did not correct stale compiled light.");
        // Budget changes must continue the independent cursor rather than restart the sampled prefix.
        renderer.SetCompiledDiffuseProbes(field);
        for (int frame = 0; frame < 8; frame++)
        {
            int budget = frame % 2 == 0 ? 8 : 24;
            var commands = Enumerable.Range(0, 4).Select(region => new DiffuseRegionCommand3D(
                region == 0 ? DiffuseRegionStrategy3D.Residual : DiffuseRegionStrategy3D.Compiled, region == 0 ? budget : 0, 0));
            renderer.DynamicDiffuse = options with { Mode = DynamicDiffuseMode3D.CompiledResidual, RegionalPlan = new(new(), commands) };
            scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, true);
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        }
        var alternating = renderer.ReadLightingForQualification();
        double alternatingError = Rmse(alternating.LinearRgb, endpoints["full-128"]);
        Require(alternatingError < .001, "Changing regional budgets lost directional coverage.");
        float[] cache = renderer.ReadDiffuseCacheForQualification();
        double negative = 0;
        var partition = new DiffuseRegionPartition3D();
        for (int probe = 0; probe < grid.Count; probe++)
        {
            if (partition.RegionForProbe(grid, probe) != 0) continue;
            for (int direction = 0; direction < grid.RaysPerProbe; direction++)
                for (int channel = 0; channel < 3; channel++)
                    negative += Math.Max(0, -cache[(probe * grid.RaysPerProbe + direction) * 4 + channel]);
        }
        Require(negative > 1, "Regional residuals failed to subtract stale compiled light.");
        SaveSamplingImage(plant, assets, output, "alternating-budget", alternating);
        renderer.DynamicDiffuse = options with { Mode = DynamicDiffuseMode3D.CompiledResidual,
            RegionalPlan = new(new(), Enumerable.Range(0, 4).Select(region => new DiffuseRegionCommand3D(
                region == 0 ? DiffuseRegionStrategy3D.Runtime : DiffuseRegionStrategy3D.Compiled, region == 0 ? 128 : 0, 0))) };
        for (int frame = 0; frame < 3; frame++)
        {
            scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, true, encloseProbe: true);
            renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
        }
        Require(renderer.CompletedDiffuseRegions!.Regions[0].BaselineChange >= 1,
            "Changed backface validity was lost by RGB-only regional telemetry.");
        object performance = MeasureRegional(plant, assets, box, lights, camera, options, field, output);
        pairs.Add(new { Name = "identity", Reference = "moved-reference.png", Test = "moved-reference.png", Group = "identity" });
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        { Qualification = "regional-diffuse", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        { Accepted = true, FullR4Complete = false, Device = plant.Facts.PhysicalDeviceName, Cases = rows,
            MaximumRayUpdates = maxWork, RegionalVsFullBasisRmse = equivalent, AlternatingBudgetVsFullRmse = alternatingError, NegativeCorrection = negative, BackfaceValidityMeasured = true, Policy = policyTrace, Performance = performance,
            Scope = "Four optically isolated rooms; caller-certified region-0 radiance influence; global sources invalidate all; no default promotion." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_R4_REGIONAL_PASSED " + output);
    }

    private static Matrix4x4 RegionalMatrix(ReferenceCamera3D camera, int width, int height)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.VerticalFieldOfViewRadians, (float)width / height, .1f, 100);
        projection.M22 *= -1;
        return Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up) * projection;
    }

    private static Native3DScene RegionalScene(NativeMesh3D box, NativeInstanceSet3D? previous, bool moved, bool encloseProbe = false)
    {
        var white = new ModelMaterial("regional-white") { BaseColor = new(.7f, .7f, .7f, 1), Metallic = 0, Roughness = 1, DoubleSided = true };
        var red = white with { Slot = "regional-red", BaseColor = new(.8f, .06f, .03f, 1) };
        var items = new List<SceneInstance3D>();
        void Add(string id, Vector3 center, Vector3 size, ModelMaterial material)
        {
            items.Add(new(id, box, material, Matrix4x4.CreateScale(size * .5f) * Matrix4x4.CreateTranslation(center)));
        }
        Add("floor", new(0, -.1f, 0), new(8.2f, .2f, 8.2f), white);
        Add("west", new(-4, 1.5f, 0), new(.2f, 3, 8.2f), red);
        Add("east", new(4, 1.5f, 0), new(.2f, 3, 8.2f), white);
        Add("north", new(0, 1.5f, -4), new(8.2f, 3, .2f), red);
        Add("south", new(0, 1.5f, 4), new(8.2f, 3, .2f), white);
        Add("divide-x", new(0, 1.5f, 0), new(.2f, 3, 8.2f), white);
        Add("divide-z", new(0, 1.5f, 0), new(8.2f, 3, .2f), white);
        Add("moving", moved ? new(-2, 1.25f, -1.6f) : new(-8, 1.25f, -1.6f), new(3, 2.5f, encloseProbe ? 1.3f : .25f), white);
        return new([], []) { Instances = new NativeInstanceSet3D(items, previous) };
    }

    private static object MeasureRegional(AurelianVulkanPlant plant, GameAssets assets, NativeMesh3D box,
        LocalLight3D[] lights, ReferenceCamera3D camera, DynamicDiffuseLighting3D options, CompiledDiffuseProbeField3D field, string output)
    {
        using var target = new VulkanNativeFrameTarget(plant, 1280, 720);
        using var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.LocalLights = lights;
        renderer.Settings = Settings();
        renderer.SetCompiledDiffuseProbes(field);
        var matrix = RegionalMatrix(camera, 1280, 720);
        var rows = new List<object>();
        foreach (string name in new[] { "fixed-32", "regional-stable" })
        {
            renderer.DynamicDiffuse = options with { Mode = DynamicDiffuseMode3D.CompiledResidual, RaysPerProbePerFrame = 32 };
            var policy = new NativeDiffuseRegionalPolicy3D(options.Grid!, settings: new() { MaximumRayUpdates = 2048, AuditSeconds = 5 });
            var scene = RegionalScene(box, null, false);
            var cpu = new List<double>();
            var gpu = new List<double>();
            var policyCpu = new List<double>();
            for (int frame = 0; frame < 104; frame++)
            {
                scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, false);
                long started = Stopwatch.GetTimestamp();
                if (name == "regional-stable") policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
                double policyTime = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var result = renderer.Render(scene, matrix, camera.Eye, Clear);
                if (name == "regional-stable") policy.AfterFrame(result);
                if (frame < 40) continue;
                cpu.Add(result.CpuTimings!.PrepareRecordMilliseconds + result.CpuTimings.SubmitMilliseconds + policyTime);
                policyCpu.Add(policyTime);
                if (result.CompletedGpuFrame is { } complete)
                    gpu.Add((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);
            }
            rows.Add(new { Case = name, MedianCpuMilliseconds = Median(cpu), MedianGpuMilliseconds = Median(gpu), MedianPolicyMilliseconds = Median(policyCpu) });
            Console.WriteLine($"Regional 720p {name}: CPU {Median(cpu):F3}ms, GPU {Median(gpu):F3}ms, policy {Median(policyCpu):F3}ms");
            scene = RegionalScene(box, (NativeInstanceSet3D)scene.Instances, false);
            if (name == "regional-stable") policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
            var image = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
            NativeGameGraphics.WritePng(Path.Combine(output, name + "-720p.png"), 1280, 720, image.Pixels!);
        }
        return rows;
    }
}
