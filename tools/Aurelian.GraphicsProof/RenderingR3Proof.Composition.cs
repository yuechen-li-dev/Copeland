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
    public static void RunComposition(string output)
    {
        const int width = 960;
        const int height = 540;
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: true, EnableRayQueries: true, ApplicationName: "Beacon renderer composition"));
        Require(initialization.Success, "Beacon composition needs an admitted Vulkan ray-query device.");
        using var plant = initialization.Plant!;
        using var validation = new RenderingR0Validation(plant);
        var assets = new GameAssets();
        using var game = new BeaconShellGame();
        var scene = new BeaconResidentScene3D().Build(game.World, includeViewmodel: false);
        var camera = new ReferenceCamera3D(new(4, 5.5f, 9), new(0, .4f, -1), Vector3.UnitY, .82f);
        var clip = RegionalMatrix(camera, width, height);
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        var cases = new List<object>();
        using (var baseline = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true))
        {
            foreach (bool visibility in new[] { false, true })
            {
                baseline.EnableVisibilityBuffer = visibility;
                foreach (GameGraphicsQuality tier in Enum.GetValues<GameGraphicsQuality>())
                {
                    baseline.Settings = new GameDisplaySettings(Quality: tier).ApplyQuality(game.Manifest.Graphics);
                    baseline.ResetTemporalHistory();
                    string path = visibility ? "visibility" : "material";
                    var passes = Capture(baseline, path + "-" + tier, scene);
                    Require(passes.Contains("surface-resolve") && passes.Contains("visibility-raster") == visibility
                        && !passes.Contains("local-ray-shadows"), "A default tier did not execute its intended raster and light paths.");
                    Require(passes.Contains("temporal-resolve") == (tier != GameGraphicsQuality.Low),
                        "A default tier did not execute its intended AA path.");
                }
            }
        }

        using var eyeState = new VulkanEyeExposureState3D(plant);
        var eye = new EyeExposurePrograms(assets.ComputeShader("EyeHistogram3D.v.ts"), assets.ComputeShader("EyeAdapt3D.v.ts"),
            assets.Shader("EyeToneMap3D.v.ts"), eyeState);
        using (var advanced = RenderingBindlessProof.CreateRenderer(plant, assets, target, enableCulling: true, rayShadows: true,
            renderScale: RenderScale3D.TwoThirds, eyeExposure: eye, ocularGlare: true))
        {
            advanced.EnableVisibilityBuffer = true;
            advanced.EnableHierarchicalZ = true;
            advanced.EnableRayShadows = true;
            advanced.EnvironmentSource = BeaconDiffuseLighting3D.Source;
            advanced.Environment = VulkanEnvironmentCompiler.Compile(plant, assets.Shader("EnvironmentCompile3D.v.ts"), BeaconDiffuseLighting3D.Source);
            advanced.DynamicDiffuse = BeaconDiffuseLighting3D.Runtime;
            advanced.Settings = BeaconDiffuseLighting3D.Settings with
            {
                AntiAliasing = AntiAliasing3D.Temporal,
                GlossyReflections = GlossyReflectionMode3D.HalfResolution,
                EyeExposure = new() { LowLight = new() },
                OcularGlare = new() { Strength = .35f },
            };
            string[] executed = Capture(advanced, "combined", scene);
            string[] expected = ["hzb-final-cull", "visibility-material-resolve", "local-ray-shadows",
                "glossy-reflection-reconstruction", "temporal-resolve", "eye-exposure", "ocular-glare"];
            Require(expected.All(executed.Contains), "The combined profile skipped a requested render pass.");
            var visibilityResource = advanced.CurrentFramePlan!.Graph.Resources.Single(resource => resource.Name == "local-ray-visibility");
            Require(visibilityResource.Format == "storage-f32x16" && visibilityResource.Width == advanced.RenderWidth,
                "The combined profile did not use one direct visibility record per raster pixel.");
            Require(advanced.ReadDiffuseOutputForQualification().Where((_, index) => index % 4 != 3).Any(value => value > 0),
                "The combined profile did not produce a diffuse GI field.");
        }

        validation.RequireClean();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true, Device = plant.Facts.PhysicalDeviceName, Width = width, Height = height, Cases = cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_RENDERING_COMPOSITION_PASSED " + output);

        string[] Capture(VulkanSolid3DRenderer renderer, string name, Native3DScene input)
        {
            for (int frame = 0; frame < 12; frame++)
                renderer.Render(input, clip, camera.Eye, Clear);
            var result = renderer.Render(input, clip, camera.Eye, Clear, capture: true);
            var plan = renderer.CurrentFramePlan!;
            string[] passes = plan.Graph.Passes.Select(pass => pass.Name).ToArray();
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, result.Pixels!);
            cases.Add(new
            {
                Name = name, renderer.RenderWidth, renderer.RenderHeight, result.PixelSha256,
                Passes = passes, result.GpuPassTimes, result.Retained,
            });
            return passes;
        }
    }
}
