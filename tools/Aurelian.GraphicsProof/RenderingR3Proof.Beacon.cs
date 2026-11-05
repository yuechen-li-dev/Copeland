using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;

internal static partial class RenderingR3Proof
{
    public static void RunBeaconGi(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "evidence.json"), "{\"Accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Whole Beacon GI"));
        Require(initialized.Success, "Beacon GI initialization failed.");
        using var plant = initialized.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        var source = BeaconDiffuseLighting3D.Source;
        var environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
        var lighting = ReferenceLighting3D.FromSettings(BeaconDiffuseLighting3D.Settings, source) with
        {
            LocalLights = BeaconDiffuseLighting3D.Lights.ToArray(),
        };
        using var game = new BeaconGame();
        var publisher = new BeaconResidentScene3D();
        var scene = publisher.Build(game);
        var cameras = new[]
        {
            new ReferenceCamera3D(game.CameraEye, game.CameraEye + game.Direction, Vector3.UnitY, MathF.PI / 3),
            new(new(9, 10, 14), new(0, 1, 0), Vector3.UnitY, .9f),
            new(new(-8, 1.6f, 4), new(-3, 1, -1), Vector3.UnitY, MathF.PI / 3),
        };
        using var target = new VulkanNativeFrameTarget(plant, Width, Height);
        using var renderer = BeaconRenderer(plant, assets, target, environment);
        renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
        renderer.Render(scene, RegionalMatrix(cameras[0], Width, Height), cameras[0].Eye, Clear, capture: true);
        var field = renderer.CaptureCompiledDiffuseProbes();
        var jsonOptions = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        string fieldPath = Path.Combine(output, "compiled-probes.json");
        File.WriteAllText(fieldPath, JsonSerializer.Serialize(field, jsonOptions));
        var restored = JsonSerializer.Deserialize<CompiledDiffuseProbeField3D>(File.ReadAllText(fieldPath), jsonOptions)!;
        restored.Validate();
        Require(field.Data.SequenceEqual(restored.Data), "Whole-arena compiled field round trip changed samples.");
        renderer.SetCompiledDiffuseProbes(restored);
        var pairs = new List<object>();
        var quality = new List<object>();
        for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
        {
            var camera = cameras[cameraIndex];
            var matrix = RegionalMatrix(camera, Width, Height);
            var reference = Reference(plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"), scene,
                BeaconDiffuseLighting3D.Lights.ToArray(), camera, samples: 2048, bounces: 2, lighting: lighting);
            var errors = new Dictionary<string, double>();
            var images = new Dictionary<string, ReferenceImage3D>();
            float[]? coverage = null;
            foreach (string name in new[] { "off", "compiled", "fixed-32", "full-128", "regional" })
            {
                var policy = BeaconDiffuseLighting3D.Policy();
                renderer.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime with
                {
                    Mode = BeaconMode(name), RaysPerProbePerFrame = name == "full-128" ? 128 : 32,
                };
                for (int frame = 0; frame < 24; frame++)
                {
                    scene = publisher.Build(game);
                    if (name == "regional") policy.BeforeFrame(renderer, scene, TimeSpan.FromSeconds(1d / 60));
                    var result = renderer.Render(scene, matrix, camera.Eye, Clear, capture: true);
                    if (name == "regional") policy.AfterFrame(result);
                }
                var image = renderer.ReadLightingForQualification();
                coverage = renderer.ReadLightMotionForQualification();
                images[name] = image;
                string capture = "view-" + cameraIndex + "-" + name;
                SaveSamplingImage(plant, assets, output, capture, image);
                pairs.Add(new { Name = capture, Reference = "view-" + cameraIndex + "-reference.png", Test = capture + ".png", Group = capture });
                if (name == "regional")
                    Require(policy.Controller.States.All(state => state.Applied == DiffuseRegionStrategy3D.Compiled), "Static Beacon did not reuse its compiled whole-arena field.");

            }
            SaveSamplingImage(plant, assets, output, "view-" + cameraIndex + "-reference", PresentSkyReference(reference, coverage!));
            foreach (var pair in images)
            {
                errors[pair.Key] = SkySurfaceError(pair.Value, reference, coverage!);
                Console.WriteLine($"Beacon view {cameraIndex} {pair.Key}: surface HDR RMSE {errors[pair.Key]:F6}");
            }
            Require(errors["full-128"] < errors["off"], "Beacon GI failed to improve an authored arena view against the independent reference.");
            double match = Rmse(images["regional"].LinearRgb, images["full-128"].LinearRgb);
            Require(match < .001, "Static whole-arena compiled/regional result differs from full runtime.");
            quality.Add(new { View = cameraIndex, Errors = errors, RegionalVsRuntimeRmse = match });
        }
        object motion = CheckBeaconMotion(plant, assets, environment, restored, output, pairs);
        object performance = MeasureBeaconGi(plant, assets, environment, restored, output);
        pairs.Add(new { Name = "identity", Reference = "view-0-reference.png", Test = "view-0-reference.png", Group = "identity" });
        validation.RequireClean();
        File.WriteAllText(Path.Combine(output, "metrics-manifest.json"), JsonSerializer.Serialize(new
        { Qualification = "beacon-diffuse", DynamicRange = "LDR", InputColor = "sRGB", Ppd = 67, Pairs = pairs }, jsonOptions));
        File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true, FullR4Complete = false, DefaultPromoted = false, SelectedBeaconGiMode = "FullRuntime128",
            Device = plant.Facts.PhysicalDeviceName, Quality = quality, Motion = motion, Performance = performance,
            NumericCompiledBytes = field.Data.Length * 4, NumericBytesPerSquareMetre = field.Data.Length * 4d / (22 * 22),
            ProbeCount = field.Grid.Count, MaximumDirectionalUpdates = field.Grid.Count * 32,
            Scope = "Complete authored Beacon rigid arena, beacons, gate, weapon, creatures and bolts; no humanoid skinning. Unknown influence conservatively invalidates all regions.",
        }, jsonOptions));
        Console.WriteLine("AURELIAN_BEACON_GI_PASSED " + output);
    }

    private static VulkanSolid3DRenderer BeaconRenderer(AurelianVulkanPlant plant, GameAssets assets,
        VulkanNativeFrameTarget target, EnvironmentLighting environment)
    {
        var renderer = TlasRenderer(plant, assets, target, SceneAccelerationUpdateMode3D.Automatic, LocalLightSamplingMode3D.Exact);
        renderer.Settings = BeaconDiffuseLighting3D.Settings;
        renderer.Environment = environment;
        renderer.EnvironmentSource = BeaconDiffuseLighting3D.Source;
        renderer.LocalLights = BeaconDiffuseLighting3D.Lights;
        return renderer;
    }

    private static DynamicDiffuseMode3D BeaconMode(string name) => name switch
    {
        "off" => DynamicDiffuseMode3D.Off,
        "compiled" => DynamicDiffuseMode3D.CompiledOnly,
        "full-128" => DynamicDiffuseMode3D.Runtime,
        _ => DynamicDiffuseMode3D.CompiledResidual,
    };
}
